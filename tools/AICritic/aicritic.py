#!/usr/bin/env python3
"""AI Builder/Critic loop for Madlands: Claude Code builds, Codex reviews. See README.md.

  python tools/AICritic/aicritic.py check [--only tools,claude,codex,pipeline,capture]
  python tools/AICritic/aicritic.py run --task "..." [--rounds N] [--dry-run [--fake-pipeline]]
  python tools/AICritic/aicritic.py status [run-id]
  python tools/AICritic/aicritic.py approve <run-id>
  python tools/AICritic/aicritic.py discard <run-id>
"""

import argparse
import json
import sys
from pathlib import Path

if sys.version_info < (3, 11):
    sys.exit("aicritic needs Python 3.11 or newer (tomllib)")

sys.path.insert(0, str(Path(__file__).resolve().parent))

from orchestrator import config as config_mod  # noqa: E402
from orchestrator import checks, fakes, loop  # noqa: E402


def main(argv=None):
    p = argparse.ArgumentParser(prog="aicritic", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--config", help="TOML config (default: tools/AICritic/config.toml if present, else built-in defaults)")
    sub = p.add_subparsers(dest="command", required=True)

    c = sub.add_parser("check", help="validate the CLIs, the build pipeline and the screenshot capture")
    c.add_argument("--only", help=f"comma-separated subset of: {','.join(checks.ALL)}")
    c.add_argument("--worktree", help="reuse this check worktree's build for the capture check")

    r = sub.add_parser("run", help="run the Builder/Critic loop on a new aicritic/<run> branch")
    task = r.add_mutually_exclusive_group(required=True)
    task.add_argument("--task", help="the implementation task for the builder")
    task.add_argument("--task-file", help="a file holding the task")
    r.add_argument("--rounds", type=int, help="maximum evaluation rounds (1-3, at most loop.max_rounds)")
    r.add_argument("--dry-run", action="store_true", help="no AI calls: a fake builder and a fixture critic")
    r.add_argument("--fake-pipeline", action="store_true", help="with --dry-run: also fake the worktree, build and capture")
    r.add_argument("--scenario", default="improve-then-pass", choices=fakes.SCENARIOS, help="dry-run fixture behaviour")
    r.add_argument("--no-prompt", action="store_true", help="do not ask for approval at the end, even in a terminal")

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

    if args.command == "run":
        text = args.task if args.task else Path(args.task_file).read_text(encoding="utf-8").strip()
        if args.rounds is not None and not 1 <= args.rounds <= cfg["loop"]["max_rounds"]:
            sys.exit(f"--rounds must be 1..{cfg['loop']['max_rounds']}")
        state = loop.run(cfg, text, args.rounds, args.dry_run, args.fake_pipeline, args.scenario,
                         interactive=False if args.no_prompt else None)
        return {"PASS": 0, "IMPROVE": 2, "BLOCKED": 3}[state["outcome"]["verdict"]]

    if args.command == "status":
        runs = config_mod.resolve(cfg, "runs_dir")
        run_id = args.run_id
        if run_id is None:
            found = sorted((d for d in runs.iterdir() if (d / "run.json").exists()), key=lambda d: d.stat().st_mtime) if runs.exists() else []
            if not found:
                sys.exit("no runs yet")
            run_id = found[-1].name
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
