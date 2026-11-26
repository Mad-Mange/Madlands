"""The Builder/Critic loop, plus approve and discard."""

import json
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from . import BLOCKED, CRITERIA, CRITERION_TITLES, HARD_MAX_ROUNDS, IMPROVE, PASS
from . import builder, capture, critic, evidence, fakes, gitops, pipeline, report, scoring
from .config import REPO_ROOT, TOOL_DIR, load_scene, resolve

MIN_BUILDER_USD = 0.5  # do not start a builder call with less budget left than this


def utc():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def make_run_id(task):
    slug = re.sub(r"[^a-z0-9]+", "-", task.lower()).strip("-")[:32].strip("-") or "task"
    return f"{datetime.now().strftime('%Y%m%d-%H%M%S')}-{slug}"


def letters_salt(run_id, round_no):
    """Two or three letters from the run id and round, for unique character names."""
    n = int(re.sub(r"\D", "", run_id)[-6:] or "0") * 10 + round_no
    out = ""
    for _ in range(3):
        out += chr(ord("a") + n % 26)
        n //= 26
    return out


def save_state(run_dir, state):
    (Path(run_dir) / "run.json").write_text(json.dumps(state, indent=2), encoding="utf-8")


def load_state(cfg, run_id):
    run_dir = resolve(cfg, "runs_dir") / run_id
    path = run_dir / "run.json"
    if not path.exists():
        raise SystemExit(f"no run '{run_id}' in {resolve(cfg, 'runs_dir')}")
    return run_dir, json.loads(path.read_text(encoding="utf-8"))


def _issue_lines(issues):
    if not issues:
        return "None."
    return "\n".join(f"{i['rank']}. **{i['title']}** ({i['severity']}, {CRITERION_TITLES[i['criterion']]})\n"
                     f"   {i['detail']}\n   Suggested fix: {i['suggested_fix']}\n"
                     f"   Evidence: {', '.join(i['evidence']) or '–'}" for i in issues)


def feedback_prompt(cfg, task, round_no, max_rounds, review, verdict):
    rows = []
    for n in CRITERIA:
        c = review["criteria"][n]
        why = (c["rationale"] + (f" Missing: {c['missing_evidence']}" if not c["evaluable"] else "")).replace("|", "/")
        rows.append(f"| {CRITERION_TITLES[n]} | {cfg['weights'][n]}% | "
                    f"{c['score'] if c['evaluable'] else 'null'} | {c['confidence']} | {why.replace(chr(10), ' ')} |")
    ta = review["task_assessment"]
    template = (TOOL_DIR / "prompts" / "builder_feedback.md").read_text(encoding="utf-8")
    return report.fill(template, round=round_no, max_rounds=max_rounds, verdict=verdict["verdict"],
                       overall=verdict["overall"], pass_score=verdict["pass_score"], score_rows="\n".join(rows),
                       summary=review["summary"], task_assessment=f"{ta['completed']}: {ta['explanation']}",
                       in_scope_issues=_issue_lines(scoring.ranked_issues(review, in_scope_only=True)),
                       out_of_scope_issues=_issue_lines([i for i in scoring.ranked_issues(review) if not i["in_scope"]]),
                       task=task)


def critic_prompt(cfg, scene, task, manifest, images, diff_stat):
    shots = "\n".join(f"- `{s['name']}`{' (burst of ' + str(s['burst']) + ')' if int(s.get('burst', 1)) > 1 else ''}: "
                      f"{s.get('description', '')}" for s in scene["shot"])
    image_list = "\n".join(f"{i + 1}. `screenshots/{name}`: {desc}" for i, (name, desc) in enumerate(images))
    tests = manifest.get("tests") or {}
    srv = (manifest.get("capture") or {}).get("server") or {}
    load = srv.get("under_load") or {}
    facts = [f"- Tests: {tests.get('passed')} passed, {tests.get('failed')} failed, {tests.get('skipped')} skipped "
             f"of {tests.get('total')}.",
             f"- Server ticks (20 Hz target, 50 ms budget): worst average {srv.get('tick_avg_ms_max')} ms, "
             f"worst p99 {srv.get('tick_p99_ms_max')} ms, max {srv.get('tick_max_ms')} ms over "
             f"{srv.get('stats_lines')} intervals; with up to {srv.get('max_players')} players "
             f"(bots): worst average {load.get('tick_avg_ms_max')} ms, worst p99 {load.get('tick_p99_ms_max')} ms, "
             f"peak outgoing {load.get('out_kb_s_max')} KB/s.",
             f"- Server log: {srv.get('level_counts')} warnings/errors/fatal lines."]
    for s in (manifest.get("capture") or {}).get("shots", []):
        m, cl = s.get("metrics") or {}, s.get("client_log") or {}
        facts.append(f"- Client `{s['shot']}`: peak working set {m.get('peak_working_set_mb')} MB, "
                     f"CPU {m.get('avg_cpu_percent_of_machine')}% of {m.get('logical_cores')} cores, "
                     f"{cl.get('problem_lines')} problem lines / {cl.get('exceptions')} exceptions in its log.")
    template = (TOOL_DIR / "prompts" / "critic.md").read_text(encoding="utf-8")
    stat_line = diff_stat.strip().splitlines()[-1].strip() if diff_stat.strip() else "no changes"
    return report.fill(template, task=task, build_finished=manifest.get("build", {}).get("finished"),
                       base_ref=cfg["git"]["base_ref"], diff_stat_line=stat_line, scene_name=scene["name"],
                       shot_list=shots, image_list=image_list, facts="\n".join(facts),
                       pass_score=cfg["loop"]["pass_score"])


def builder_policy_problems(cfg, wt, base_commit, branch_name, base_tree, tree):
    problems = []
    if gitops.rev(wt) != base_commit:
        problems.append("the builder created a commit or moved HEAD")
    if gitops.branch(wt) != branch_name:
        problems.append(f"the builder switched away from {branch_name}")
    for _, path in gitops.name_status(wt, base_tree, tree):
        for prefix in cfg["builder"]["protected_paths"]:
            if path.replace("\\", "/").startswith(prefix):
                problems.append(f"the builder changed a protected path: {path}")
    return problems


def settle_side_effects(wt, base_tree, builder_tree):
    """Unity rewrites files while importing and building. Keeps the .meta files Unity created for assets
    the builder added (they belong with them) and puts everything else back to the builder's version."""
    post_tree = gitops.snapshot(wt, gitops.temp_index(wt, "post"))
    if post_tree == builder_tree:
        return builder_tree, [], []
    added = [p for s, p in gitops.name_status(wt, base_tree, builder_tree) if s == "A"]
    keep, revert = [], []
    for status, path in gitops.name_status(wt, builder_tree, post_tree):
        owner = path[:-5] if path.endswith(".meta") else None
        if status == "A" and owner and any(a == owner or a.startswith(owner + "/") for a in added):
            keep.append(path)
        else:
            revert.append((status, path))
    gitops.restore_paths(wt, builder_tree, revert)
    return gitops.snapshot(wt, gitops.temp_index(wt, "settled")), keep, [p for _, p in revert]


def run(cfg, task, max_rounds=None, dry_run=False, fake_pipeline=False, scenario="improve-then-pass",
        interactive=None):
    if fake_pipeline and not dry_run:
        raise SystemExit("--fake-pipeline needs --dry-run")
    max_rounds = min(max_rounds or cfg["loop"]["max_rounds"], cfg["loop"]["max_rounds"], HARD_MAX_ROUNDS)
    scene = load_scene(cfg)
    runs_dir = resolve(cfg, "runs_dir")
    run_id = make_run_id(task)
    run_dir = runs_dir / run_id
    run_dir.mkdir(parents=True)
    (run_dir / "task.md").write_text(task + "\n", encoding="utf-8")
    branch_name = cfg["git"]["branch_prefix"] + run_id
    worktree_root = resolve(cfg, "worktree_root")
    wt = (run_dir / "fake-worktree") if fake_pipeline else (worktree_root / run_id)
    started = time.time()
    deadline = started + cfg["loop"]["max_run_minutes"] * 60
    state = {"run_id": run_id, "task": task, "created": utc(), "config": cfg["_source"], "dry_run": dry_run,
             "fake_pipeline": fake_pipeline, "scenario": scenario if dry_run else None,
             "base_ref": cfg["git"]["base_ref"], "branch": branch_name, "worktree": str(wt),
             "max_rounds": max_rounds, "status": "running", "rounds": [], "outcome": None,
             "costs": {"builder_usd_total": 0.0, "critic_tokens_total": 0},
             "approval": {"state": "not_requested"}}
    save_state(run_dir, state)
    print(f"[aicritic] run {run_id}: branch {branch_name}, worktree {wt}")

    if fake_pipeline:
        fakes.init_repo(wt, branch_name)
    else:
        gitops.add_worktree(REPO_ROOT, wt, cfg["git"]["base_ref"], branch_name)
        seed = pipeline.seed_library(cfg, wt, run_dir)
        state["library_seed"] = seed
        print(f"[aicritic] Unity Library: {seed}")
    base_commit, base_tree = gitops.rev(wt), gitops.head_tree(wt)
    state["base_commit"] = base_commit
    prev_tree, session, prompt = base_tree, None, None
    stop_reason, last_verdict = None, None

    for round_no in range(1, max_rounds + 1):
        round_dir = run_dir / f"round-{round_no:02d}"
        round_dir.mkdir()
        round_start = time.time()
        doc = {"run_id": run_id, "round": round_no, "task": task, "branch": branch_name, "generated": utc(),
               "round_dir": str(round_dir), "weights": cfg["weights"], "steps": [], "shots": [], "media": {},
               "tests": None, "review": None, "tree": None, "costs": {}}
        costs = state["costs"]

        def finish(verdict, **extra):
            doc["verdict"] = verdict
            doc["generated"] = utc()
            doc["costs"].update({"builder_usd_total": costs["builder_usd_total"],
                                 "critic_tokens_total": costs["critic_tokens_total"]})
            doc.update(extra)
            report.write_round(doc, round_dir, run_dir, runs_dir)
            state["rounds"].append({"round": round_no, "verdict": verdict["verdict"], "reason": verdict["reason"],
                                    "detail": verdict["detail"], "overall": verdict.get("overall"),
                                    "tree": doc.get("tree"), "builder_usd": doc["costs"].get("builder_usd", 0.0),
                                    "critic_tokens": doc["costs"].get("critic_tokens", 0)})
            save_state(run_dir, state)
            print(f"[aicritic] round {round_no}: {verdict['verdict']} ({verdict['reason']}) {verdict['detail']}")
            return verdict

        # ---- budgets and time
        left_usd = cfg["budget"]["builder_usd_total"] - costs["builder_usd_total"]
        if left_usd < MIN_BUILDER_USD:
            stop_reason = "builder_budget_exhausted"
            last_verdict = finish(scoring.blocked("budget_exhausted", f"only ${left_usd:.2f} of the builder budget left"))
            break
        if costs["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
            stop_reason = "critic_budget_exhausted"
            last_verdict = finish(scoring.blocked("budget_exhausted", "the critic token budget is used up"))
            break
        if time.time() > deadline:
            stop_reason = "run_time_limit"
            last_verdict = finish(scoring.blocked("time_limit", "the run reached loop.max_run_minutes"))
            break

        # ---- builder
        if prompt is None:
            template = (TOOL_DIR / "prompts" / "builder_task.md").read_text(encoding="utf-8")
            prompt = report.fill(template, task=task, worktree=wt, branch=branch_name)
        budget = min(cfg["budget"]["builder_usd_per_round"], left_usd)
        print(f"[aicritic] round {round_no}: builder (budget ${budget:.2f})")
        if dry_run:
            b = fakes.builder(wt, round_no, scenario, round_dir, prompt)
        else:
            timeout = min(cfg["timeouts"]["builder"], max(60, deadline - time.time()))
            b = builder.run(cfg, wt, round_dir, prompt, budget, timeout, session)
        costs["builder_usd_total"] += b["cost_usd"]
        doc["costs"]["builder_usd"] = b["cost_usd"]
        doc["builder"] = {k: v for k, v in b.items() if k != "summary"}
        doc["builder_summary"] = b["summary"]
        session = b["session_id"] or session
        state["builder_session"] = session
        if not b["ok"]:
            stop_reason = "builder_failed"
            last_verdict = finish(scoring.blocked("builder_failed", b["error"]))
            break
        builder_tree = gitops.snapshot(wt, gitops.temp_index(wt, "builder"))
        doc["tree"] = builder_tree
        gitops.diff(wt, base_tree, builder_tree, round_dir / "diff.patch")
        gitops.diff(wt, prev_tree, builder_tree, round_dir / "round-diff.patch")
        stat = gitops.diff_stat(wt, base_tree, builder_tree)
        (round_dir / "diffstat.txt").write_text(stat + "\n", encoding="utf-8")
        doc["diff_stat"] = stat
        problems = builder_policy_problems(cfg, wt, base_commit, branch_name, base_tree, builder_tree)
        if problems:
            stop_reason = "builder_policy_violation"
            last_verdict = finish(scoring.blocked("builder_policy_violation", "; ".join(problems)))
            break

        # ---- build and tests
        print(f"[aicritic] round {round_no}: build and tests")
        if fake_pipeline:
            ok, steps, failed = fakes.pipeline(round_dir, round_no, scenario, wt)
        else:
            ok, steps, failed = pipeline.run_pipeline(cfg, wt, round_dir, round_start)
        doc["steps"] = steps
        doc["tests"] = next((s.get("tests") for s in steps if s["name"] == "dotnet_test"), None)
        if not ok:
            step = next(s for s in steps if s["name"] == failed)
            stop_reason = "required_step_failed"
            last_verdict = finish(scoring.blocked("required_step_failed", f"{failed}: {step.get('error') or 'failed'}"))
            break
        exe = pipeline.client_exe(wt)
        build_finished = exe.stat().st_mtime if exe.exists() else None

        # ---- play session
        print(f"[aicritic] round {round_no}: play session ({len(scene['shot'])} shots)")
        if fake_pipeline:
            ok, cap, failure = fakes.capture(scene, round_dir)
        else:
            ok, cap, failure = capture.run_capture(cfg, scene, wt, round_dir, letters_salt(run_id, round_no))
        doc["shots"] = cap["shots"]
        if not ok:
            stop_reason = "capture_failed"
            last_verdict = finish(scoring.blocked("capture_failed", failure))
            break

        # ---- Unity's side effects, then the evidence
        if not fake_pipeline:
            settled, kept, reverted = settle_side_effects(wt, base_tree, builder_tree)
            doc.update({"tree": settled, "pipeline_kept": kept, "pipeline_reverted": reverted})
            builder_tree = settled
        media = evidence.prepare_media(scene, round_dir)
        doc["media"] = media
        manifest = evidence.build_manifest(round_dir, {
            "run_id": run_id, "round": round_no, "branch": branch_name, "tree": builder_tree,
            "build": {"client_exe": str(exe), "client_sha256": evidence.sha256(exe) if exe.exists() else None,
                      "finished": evidence.iso(build_finished) if build_finished else None},
            "steps": steps, "tests": doc["tests"], "capture": cap, "media": media})
        stale = evidence.freshness_problems(manifest, round_start, build_finished)
        if stale:
            stop_reason = "stale_evidence"
            last_verdict = finish(scoring.blocked("stale_evidence", "; ".join(stale[:5])))
            break

        # ---- critic
        if costs["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
            stop_reason = "critic_budget_exhausted"
            last_verdict = finish(scoring.blocked("budget_exhausted", "the critic token budget is used up"))
            break
        stage = worktree_root / "_critic" / run_id / f"round-{round_no:02d}"
        stage = evidence.stage_for_critic(round_dir, stage, {"diff.patch": round_dir / "diff.patch",
                                                              "task.md": run_dir / "task.md"})
        images = evidence.critic_images(scene, media, cfg["critic"]["max_images"])
        cprompt = critic_prompt(cfg, scene, task, manifest, images, stat)
        before_wt, before_stage = gitops.state_fingerprint(wt), evidence.folder_fingerprint(stage)
        print(f"[aicritic] round {round_no}: critic ({len(images)} images)")
        if dry_run:
            c = fakes.critic(round_no, scenario, round_dir / "critic", cprompt, wt)
        else:
            timeout = min(cfg["timeouts"]["critic"], max(60, deadline - time.time()))
            c = critic.run(cfg, stage, round_dir / "critic", cprompt,
                           [stage / "screenshots" / name for name, _ in images], timeout)
        tokens = int(c["usage"].get("total_tokens") or 0)
        costs["critic_tokens_total"] += tokens
        doc["costs"]["critic_tokens"] = tokens
        doc["critic"] = {k: v for k, v in c.items() if k != "review"}
        after_wt, after_stage = gitops.state_fingerprint(wt), evidence.folder_fingerprint(stage)
        if after_wt["hash"] != before_wt["hash"] or after_stage != before_stage:
            stop_reason = "critic_integrity_violation"
            what = "the worktree" if after_wt["hash"] != before_wt["hash"] else "its evidence folder"
            last_verdict = finish(scoring.blocked("critic_integrity_violation",
                                                  f"files changed in {what} while the critic ran; the review is discarded"))
            break
        if not c["ok"]:
            stop_reason = "critic_failed"
            last_verdict = finish(scoring.blocked("critic_failed", c["error"]))
            break
        try:
            warnings = scoring.validate_review(c["review"], cfg["loop"]["pass_score"])
        except scoring.ReviewError as e:
            stop_reason = "critic_invalid_review"
            last_verdict = finish(scoring.blocked("critic_invalid_review", str(e)))
            break
        verdict = scoring.score(c["review"], cfg["weights"], cfg["loop"]["pass_score"])
        last_verdict = finish(verdict, review=c["review"], review_warnings=warnings)
        prev_tree = builder_tree
        if verdict["verdict"] == PASS:
            stop_reason = "pass"
            break
        if verdict["verdict"] == BLOCKED:
            stop_reason = "blocked"
            break
        if round_no == max_rounds:
            stop_reason = "max_rounds"
            break
        prompt = feedback_prompt(cfg, task, round_no + 1, max_rounds, c["review"], verdict)

    final_tree = state["rounds"][-1]["tree"] if state["rounds"] else None
    state.update({"status": "finished", "finished": utc(), "final_tree": final_tree,
                  "outcome": {"verdict": last_verdict["verdict"] if last_verdict else BLOCKED,
                              "stop_reason": stop_reason, "rounds": len(state["rounds"]),
                              "overall": last_verdict.get("overall") if last_verdict else None}})
    has_changes = bool(final_tree) and final_tree != base_tree
    if dry_run:
        # Dry-run changes come from the fake builder: never something to accept.
        state["approval"] = {"state": "dry_run"}
    else:
        state["approval"] = {"state": "pending" if has_changes else "nothing_to_approve"}
    save_state(run_dir, state)
    print_summary(run_dir, state)
    if interactive is None:
        interactive = sys.stdin.isatty() and not dry_run
    if interactive and state["approval"]["state"] == "pending":
        approve(cfg, run_id, None, prompt_user=True)
    return state


def print_summary(run_dir, state):
    o = state["outcome"]
    print("")
    print(f"[aicritic] run {state['run_id']} finished: {o['verdict']} after {o['rounds']} round(s), "
          f"stop reason {o['stop_reason']}, score {o['overall'] if o['overall'] is not None else '–'}")
    for r in state["rounds"]:
        print(f"  round {r['round']}: {r['verdict']:<7} {r['reason']:<26} "
              f"score {r['overall'] if r['overall'] is not None else '–':<5} builder ${r['builder_usd']:.2f}, "
              f"critic {r['critic_tokens']:,} tokens")
    print(f"  builder total ${state['costs']['builder_usd_total']:.2f}, "
          f"critic total {state['costs']['critic_tokens_total']:,} tokens")
    print(f"  report: {Path(run_dir) / 'latest-review.md'}")
    if state["approval"]["state"] == "pending":
        print(f"  The changes wait on branch {state['branch']} (worktree {state['worktree']}), not committed.")
        print(f"  Accept:  python tools/AICritic/aicritic.py approve {state['run_id']}")
        print(f"  Discard: python tools/AICritic/aicritic.py discard {state['run_id']}")


def _confirmed(run_id, confirm, action, prompt_user):
    if confirm is not None:
        return confirm == run_id
    if not prompt_user or not sys.stdin.isatty():
        print(f"[aicritic] {action} needs a person: run it in a terminal, or pass --confirm {run_id}")
        return False
    answer = input(f"Type the run id to {action} ({run_id}), or press Enter to cancel: ").strip()
    return answer == run_id


def approve(cfg, run_id, confirm, prompt_user=True):
    """Commits the run's final changes on its own branch. Never merges anything."""
    run_dir, state = load_state(cfg, run_id)
    if state["approval"]["state"] != "pending":
        raise SystemExit(f"run {run_id} has nothing pending (approval state: {state['approval']['state']})")
    wt = Path(state["worktree"])
    o = state["outcome"]
    print(f"Run {run_id}: {o['verdict']} after {o['rounds']} round(s), score {o['overall']}, "
          f"stop reason {o['stop_reason']}")
    print(gitops.git(["diff", "--stat", state["base_commit"]], wt) or "(no tracked changes)")
    print(f"Report: {run_dir / 'latest-review.md'}")
    current = gitops.snapshot(wt, gitops.temp_index(wt, "approve"))
    if current != state["final_tree"]:
        raise SystemExit("the worktree changed after the run ended; review it by hand before committing")
    if not _confirmed(run_id, confirm, "approve", prompt_user):
        print("[aicritic] not approved; nothing changed")
        return False
    message = (f"AI builder: {state['task'].splitlines()[0][:60]}\n\n"
               f"aicritic run {run_id}: {o['verdict']} after {o['rounds']} round(s), "
               f"weighted score {o['overall'] if o['overall'] is not None else 'none'}.\n"
               f"Approved manually; the reviews are in builds/aicritic/{run_id}/.\n\n"
               f"Co-Authored-By: Claude <noreply@anthropic.com>\n")
    commit = gitops.commit_all(wt, message)
    state["approval"] = {"state": "approved", "at": utc(), "commit": commit}
    save_state(run_dir, state)
    print(f"[aicritic] committed {commit[:10]} on {state['branch']}. Nothing was merged: merging into "
          f"{state['base_ref']} is up to you.")
    return True


def discard(cfg, run_id, confirm):
    run_dir, state = load_state(cfg, run_id)
    if state.get("fake_pipeline"):
        raise SystemExit("dry runs with a fake pipeline have no worktree or branch to discard")
    if not _confirmed(run_id, confirm, "discard", True):
        print("[aicritic] not discarded")
        return False
    wt = Path(state["worktree"])
    if wt.exists():
        gitops.remove_worktree(REPO_ROOT, wt)
    if gitops.branch_exists(REPO_ROOT, state["branch"]):
        gitops.delete_branch(REPO_ROOT, state["branch"])
    state["approval"] = {"state": "discarded", "at": utc()}
    save_state(run_dir, state)
    print(f"[aicritic] removed worktree {wt} and branch {state['branch']}; the reports stay in {run_dir}")
    return True
