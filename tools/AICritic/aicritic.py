#!/usr/bin/env python3
"""AI Builder/Critic tools for Madlands: Claude Code builds, Codex reviews and playtests. See README.md.

  check        validate the CLIs, the build pipeline and the screenshot capture
  review       /critic: review the latest changes (a commit, or a snapshot of uncommitted work)
  audit        /critic-audit: review the game as it is
  explore      /critic-explore: Codex plays the game through the Playtest Controller
  loop-start   /critic-loop: open a fix loop whose builder is the calling Claude session
  loop-review  /critic-loop: evaluate the loop's worktree as the next round (at most 3)
  report       /critic-report: show the latest review, scores, issues and screenshots
  run          the original loop with `claude -p` as builder (terminal only)
  status / approve / discard
"""

import argparse
import json
import sys
from pathlib import Path

if sys.version_info < (3, 11):
    sys.exit("aicritic needs Python 3.11 or newer (tomllib)")

sys.path.insert(0, str(Path(__file__).resolve().parent))

from orchestrator import config as config_mod  # noqa: E402
from orchestrator import checks, explore, fakes, loop, review  # noqa: E402

EXIT = {"PASS": 0, "IMPROVE": 2, "BLOCKED": 3}


def _target_args(p):
    p.add_argument("--ref", default="HEAD", help="the commit to build (default HEAD)")
    p.add_argument("--snapshot", action="store_true", help="build HEAD plus the uncommitted changes instead")
    p.add_argument("--paths", nargs="+", help="with --snapshot: only the uncommitted changes under these paths")
    p.add_argument("--keep-worktree", action="store_true", help="keep the temporary worktree afterwards")


def _dry_args(p, default_scenario):
    p.add_argument("--dry-run", action="store_true", help="no AI calls: fixture answers instead")
    p.add_argument("--fake-pipeline", action="store_true", help="with --dry-run: also fake the worktree, build and game")
    p.add_argument("--scenario", default=default_scenario, choices=fakes.SCENARIOS, help="dry-run fixture behaviour")


def main(argv=None):
    p = argparse.ArgumentParser(prog="aicritic", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--config", help="TOML config (default: tools/AICritic/config.toml if present, else built-in defaults)")
    sub = p.add_subparsers(dest="command", required=True)

    c = sub.add_parser("check", help="validate the CLIs, the build pipeline and the screenshot capture")
    c.add_argument("--only", help=f"comma-separated subset of: {','.join(checks.ALL)}")
    c.add_argument("--worktree", help="reuse this check worktree's build for the capture check")

    rv = sub.add_parser("review", help="/critic: review the latest changes")
    _target_args(rv)
    rv.add_argument("--base", help="compare with this commit (default: HEAD for --snapshot, else the ref's parent)")
    rv.add_argument("--task", help="what the changes were meant to do")
    _dry_args(rv, "pass")

    au = sub.add_parser("audit", help="/critic-audit: review the game as it is")
    _target_args(au)
    au.add_argument("--focus", help="an optional focus for the audit")
    _dry_args(au, "pass")

    ex = sub.add_parser("explore", help="/critic-explore: Codex plays the game through the Playtest Controller")
    ex.add_argument("scenarios", nargs="*", default=["world"], help=f"one or more of: {', '.join(explore.SCENARIO_NAMES)}, all")
    ex.add_argument("--mode", choices=["ai", "scripted"], default="ai", help="Codex picks the actions, or the scenario's script")
    ex.add_argument("--max-actions", type=int, help="actions per scenario (AI mode)")
    ex.add_argument("--max-minutes", type=float, help="time limit for the whole exploration")
    ex.add_argument("--no-review", action="store_true", help="skip the final Codex review (scripted smoke tests)")
    ex.add_argument("--reuse-build", metavar="WORKTREE", help="play the playtest client already built in this worktree")
    _target_args(ex)
    ex.add_argument("--dry-run", action="store_true", help="no AI calls: scripted actions and a fixture review")
    ex.add_argument("--fake-client", action="store_true", help="with --dry-run: a simulated client, no build (tool tests only)")

    ls = sub.add_parser("loop-start", help="/critic-loop: open a fix loop on a new aicritic/<run> branch")
    ls.add_argument("--task", required=True, help="what to fix or build")
    ls.add_argument("--from-run", help="seed the task with the issues of an audit, review or explore run")
    ls.add_argument("--rounds", type=int, help="maximum evaluation rounds (1-3)")

    lr = sub.add_parser("loop-review", help="/critic-loop: evaluate the loop's worktree as its next round")
    lr.add_argument("run_id")

    rp = sub.add_parser("report", help="/critic-report: show the latest review (or a run's)")
    rp.add_argument("run_id", nargs="?")

    r = sub.add_parser("run", help="the loop with `claude -p` as builder, on a new aicritic/<run> branch")
    task = r.add_mutually_exclusive_group(required=True)
    task.add_argument("--task", help="the implementation task for the builder")
    task.add_argument("--task-file", help="a file holding the task")
    r.add_argument("--rounds", type=int, help="maximum evaluation rounds (1-3, at most loop.max_rounds)")
    _dry_args(r, "improve-then-pass")
    r.add_argument("--no-prompt", action="store_true", help="do not ask for approval at the end, even in a terminal")
    r.add_argument("--allow-nested", action="store_true", help="allow starting `claude -p` from inside Claude Code")

    s = sub.add_parser("status", help="show a run (default: the newest)")
    s.add_argument("run_id", nargs="?")

    a = sub.add_parser("approve", help="commit a finished run's changes on its own branch (never merges)")
    a.add_argument("run_id")
    a.add_argument("--confirm", metavar="RUN_ID", help="non-interactive confirmation: repeat the run id")

    d = sub.add_parser("discard", help="remove a run's worktree and branch (the reports stay)")
    d.add_argument("run_id")
    d.add_argument("--confirm", metavar="RUN_ID", help="non-interactive confirmation: repeat the run id")

    args = p.parse_args(argv)
    try:
        cfg = config_mod.load(args.config)
    except config_mod.ConfigError as e:
        sys.exit(f"config error: {e}")

    if args.command == "check":
        only = [x.strip() for x in args.only.split(",")] if args.only else None
        if only and (bad := [x for x in only if x not in checks.ALL]):
            sys.exit(f"unknown checks: {', '.join(bad)}")
        return 0 if checks.run_checks(cfg, only, args.worktree)["ok"] else 1

    if args.command in ("review", "audit"):
        if args.paths and not args.snapshot:
            sys.exit("--paths needs --snapshot")
        state = review.review(cfg, "change" if args.command == "review" else "audit",
                              task=args.task if args.command == "review" else args.focus,
                              ref=args.ref, snapshot=args.snapshot, paths=args.paths,
                              base=getattr(args, "base", None), keep_worktree=args.keep_worktree,
                              dry_run=args.dry_run, fake_pipeline=args.fake_pipeline, scenario=args.scenario)
        return EXIT[state["outcome"]["verdict"]]

    if args.command == "explore":
        if args.paths and not args.snapshot:
            sys.exit("--paths needs --snapshot")
        if args.fake_client and not args.dry_run:
            sys.exit("--fake-client needs --dry-run")
        state = explore.explore(cfg, args.scenarios, mode=args.mode, max_actions=args.max_actions,
                                max_minutes=args.max_minutes, ref=args.ref, snapshot=args.snapshot, paths=args.paths,
                                keep_worktree=args.keep_worktree, dry_run=args.dry_run,
                                fake_client=args.fake_client, final_review=not args.no_review,
                                reuse_build=args.reuse_build)
        return EXIT[state["outcome"]["verdict"]]

    if args.command == "loop-start":
        loop.loop_start(cfg, args.task, args.from_run, args.rounds)
        return 0

    if args.command == "loop-review":
        state = loop.loop_review(cfg, args.run_id)
        last = state["rounds"][-1]["verdict"]
        return EXIT[last]

    if args.command == "report":
        review.show_report(cfg, args.run_id)
        return 0

    if args.command == "run":
        if not args.dry_run:
            loop.refuse_nested(args.allow_nested)
        text = args.task if args.task else Path(args.task_file).read_text(encoding="utf-8").strip()
        if args.rounds is not None and not 1 <= args.rounds <= cfg["loop"]["max_rounds"]:
            sys.exit(f"--rounds must be 1..{cfg['loop']['max_rounds']}")
        state = loop.run(cfg, text, args.rounds, args.dry_run, args.fake_pipeline, args.scenario,
                         interactive=False if args.no_prompt else None)
        return EXIT[state["outcome"]["verdict"]]

    if args.command == "status":
        run_id = args.run_id or loop.latest_run_id(cfg)
        if run_id is None:
            sys.exit("no runs yet")
        run_dir, state = loop.load_state(cfg, run_id)
        if state.get("outcome"):
            loop.print_summary(run_dir, state)
        else:
            print(json.dumps(state, indent=2))
        return 0

    if args.command == "approve":
        return 0 if loop.approve(cfg, args.run_id, args.confirm) else 1

    if args.command == "discard":
        return 0 if loop.discard(cfg, args.run_id, args.confirm) else 1
    return 1


if __name__ == "__main__":
    sys.exit(main())
