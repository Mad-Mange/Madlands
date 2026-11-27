"""Single reviews that never change the game: `review` (/critic: the latest changes) and `audit`
(/critic-audit: the game as it is). Both build a detached worktree, run the shared evaluation once and
remove the worktree again unless asked to keep it."""

import json
import re
import time
from pathlib import Path

from . import CRITERIA, CRITERION_TITLES, fakes, gitops, pipeline, scoring
from .config import REPO_ROOT, load_scene, resolve
from .evaluate import evaluate
from .loop import _ctx, _diff_files, _finish_run, finish_round, load_state, latest_run_id, make_run_id, new_round_doc, new_state, save_state


def _prepare(cfg, run_dir, wt, ref, snapshot, paths, fake):
    """Creates the detached worktree. Returns (commit under review, description)."""
    if fake:
        fakes.init_repo(wt, "review")
        return gitops.rev(wt), "fake repository"
    if snapshot:
        commit = gitops.snapshot_commit(REPO_ROOT, paths, "aicritic snapshot of uncommitted changes")
        what = "HEAD plus the uncommitted changes" + (f" under {', '.join(paths)}" if paths else "")
    else:
        commit = gitops.rev(REPO_ROOT, ref)
        what = f"{ref} ({commit[:10]})"
    gitops.add_worktree(REPO_ROOT, wt, commit)
    return commit, what


def review(cfg, mode, task=None, ref="HEAD", snapshot=False, paths=None, base=None, keep_worktree=False,
           dry_run=False, fake_pipeline=False, scenario="pass"):
    """mode 'change' (the diff against base) or 'audit' (the whole game, no diff)."""
    scene = load_scene(cfg)
    label = task or ("game audit" if mode == "audit" else "review of the latest changes")
    run_id = make_run_id(label, "audit-" if mode == "audit" else "review-")
    run_dir = resolve(cfg, "runs_dir") / run_id
    run_dir.mkdir(parents=True)
    wt = (run_dir / "fake-worktree") if fake_pipeline else (resolve(cfg, "worktree_root") / run_id)
    commit, what = _prepare(cfg, run_dir, wt, ref, snapshot, paths, fake_pipeline)
    if mode == "audit":
        task_text = task or ("Audit the game as it is: no particular change is under review. Judge the current "
                             "build against the criteria and name the issues that matter most.")
    else:
        task_text = task or "Review the latest implementation changes shown in diff.patch."
    (run_dir / "task.md").write_text(task_text + "\n", encoding="utf-8")
    state = new_state(cfg, run_id, "audit" if mode == "audit" else "review", task_text, wt,
                      reviewed=what, commit=commit, dry_run=dry_run, fake_pipeline=fake_pipeline, max_rounds=1)
    if not fake_pipeline:
        state["library_seed"] = pipeline.seed_library(cfg, wt, run_dir)
    save_state(run_dir, state)
    print(f"[aicritic] {state['kind']} {run_id}: {what}, worktree {wt}", flush=True)

    tree = gitops.head_tree(wt)
    if mode == "change" and not fake_pipeline:
        # A snapshot is compared with HEAD (the uncommitted changes); a commit with its parent.
        base_commit = gitops.rev(REPO_ROOT, base or ("HEAD" if snapshot else f"{commit}~1"))
        base_tree = gitops.git(["rev-parse", f"{base_commit}^{{tree}}"], REPO_ROOT)
        state["base_commit"] = base_commit
    else:
        base_tree = tree
    round_dir = run_dir / "round-01"
    round_dir.mkdir()
    round_start = time.time()
    deadline = round_start + cfg["loop"]["max_run_minutes"] * 60
    doc = new_round_doc(state, 1, round_dir, cfg)
    doc["tree"] = tree
    stat_text = _diff_files(wt, round_dir, base_tree, base_tree, tree) if mode == "change" else ""
    doc["diff_stat"] = stat_text
    verdict, fields = evaluate(cfg, scene, wt, _ctx(
        cfg, state, run_dir, round_dir, 1, round_start, deadline, base_tree, tree, mode,
        dry_run=dry_run, fake_pipeline=fake_pipeline, scenario=scenario,
        diff_file=round_dir / "diff.patch" if mode == "change" else None, diff_stat=stat_text))
    finish_round(cfg, state, run_dir, doc, verdict, fields)
    stop = "pass" if verdict["verdict"] == "PASS" else ("blocked" if verdict["verdict"] == "BLOCKED" else "reviewed")
    _finish_run(cfg, state, run_dir, verdict, stop, base_tree, dry_run, changes_possible=False)
    if not keep_worktree and not fake_pipeline and wt.exists():
        gitops.remove_worktree(REPO_ROOT, wt, resolve(cfg, "worktree_root"))
        state["worktree_removed"] = True
        save_state(run_dir, state)
    return state


# ---------------------------------------------------------------------------- report

def show_report(cfg, run_id=None):
    """Prints the latest review (of a run, or of all runs): verdict, scores, issues and screenshots."""
    run_id = run_id or latest_run_id(cfg)
    if run_id is None:
        raise SystemExit("no runs yet")
    run_dir, state = load_state(cfg, run_id)
    latest = run_dir / "latest-review.json"
    if not latest.exists():
        raise SystemExit(f"{run_id} has no review yet (status {state['status']})")
    doc = json.loads(latest.read_text(encoding="utf-8"))
    v = doc["verdict"]
    print(f"{state.get('kind', 'run')} {run_id}, round {doc['round']}: {v['verdict']} ({v['reason']}) {v['detail']}")
    print(f"Task: {state['task'].splitlines()[0]}")
    review = doc.get("review")
    if review:
        for n in CRITERIA:
            c = review["criteria"][n]
            print(f"  {CRITERION_TITLES[n]:<30} {doc['weights'][n]:>3}%  "
                  f"{c['score'] if c['evaluable'] else 'NOT EVALUATED':<14} {c['confidence']}")
        print(f"Summary: {review['summary']}")
        for i in scoring.ranked_issues(review):
            print(f"  {i['rank']}. [{i['severity']}] {i['title']} ({i['criterion']}"
                  f"{', in scope' if i['in_scope'] else ''})")
            if i.get("reproduction"):
                print(f"     Reproduce: {i['reproduction']}")
            print(f"     Fix: {i['suggested_fix']}")
    shots = Path(doc["round_dir"]) / "evidence" / "screenshots"
    if shots.exists():
        # Single shots and contact sheets; burst frames (name_3.png) are inside the sheets.
        pictures = sorted(p for p in shots.rglob("*.png") if not re.search(r"_\d+$", p.stem))
        print("Screenshots:")
        for p in pictures[:12]:
            print(f"  {p}")
    print(f"Report: {run_dir / 'latest-review.md'}")
    if state.get("approval", {}).get("state") == "pending":
        print(f"Pending approval: python tools/AICritic/aicritic.py approve {run_id}")
    return doc
