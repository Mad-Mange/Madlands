"""The Builder/Critic loops and the run lifecycle (approve, discard).

  run          Claude Code CLI (`claude -p`) is the builder: for terminal use only.
  loop-start   creates a run whose builder is the calling session (the /critic-loop slash command), so no
  loop-review  second Claude is ever started from inside Claude Code; loop-review evaluates one round.
"""

import json
import os
import re
import stat
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from . import BLOCKED, CRITERIA, CRITERION_TITLES, HARD_MAX_ROUNDS, PASS
from . import builder, fakes, gitops, pipeline, report, scoring
from .config import REPO_ROOT, TOOL_DIR, load_scene, resolve
from .evaluate import evaluate

MIN_BUILDER_USD = 0.5  # do not start a builder call with less budget left than this


def utc():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def make_run_id(task, prefix=""):
    slug = re.sub(r"[^a-z0-9]+", "-", task.lower()).strip("-")[:32].strip("-") or "task"
    return f"{datetime.now().strftime('%Y%m%d-%H%M%S')}-{prefix}{slug}"


def letters_salt(run_id, round_no):
    """Three letters from the run id and round, for unique character names."""
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


def latest_run_id(cfg, predicate=None):
    runs = resolve(cfg, "runs_dir")
    if not runs.exists():
        return None
    found = []
    for d in runs.iterdir():
        f = d / "run.json"
        if f.exists():
            state = json.loads(f.read_text(encoding="utf-8"))
            if predicate is None or predicate(state):
                found.append((state.get("created", ""), d.name))
    return max(found)[1] if found else None


def new_state(cfg, run_id, kind, task, wt, branch=None, **extra):
    return {"run_id": run_id, "kind": kind, "task": task, "created": utc(), "config": cfg["_source"],
            "base_ref": cfg["git"]["base_ref"], "branch": branch, "worktree": str(wt), "status": "running",
            "rounds": [], "outcome": None, "costs": {"builder_usd_total": 0.0, "critic_tokens_total": 0},
            "approval": {"state": "not_requested"}, **extra}


def _issue_lines(issues):
    if not issues:
        return "None."
    return "\n".join(f"{i['rank']}. **{i['title']}** ({i['severity']}, {CRITERION_TITLES[i['criterion']]})\n"
                     f"   {i['detail']}\n"
                     + (f"   Reproduction: {i['reproduction']}\n" if i.get("reproduction") else "")
                     + f"   Suggested fix: {i['suggested_fix']}\n"
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


def builder_policy_problems(cfg, wt, base_commit, branch_name, base_tree, tree):
    problems = []
    if gitops.rev(wt) != base_commit:
        problems.append("the builder created a commit or moved HEAD")
    if branch_name and gitops.branch(wt) != branch_name:
        problems.append(f"the builder switched away from {branch_name}")
    for _, path in gitops.name_status(wt, base_tree, tree):
        for prefix in cfg["builder"]["protected_paths"]:
            if path.replace("\\", "/").startswith(prefix):
                problems.append(f"the builder changed a protected path: {path}")
    return problems


def new_round_doc(state, round_no, round_dir, cfg):
    return {"run_id": state["run_id"], "kind": state.get("kind", "builder-loop"), "round": round_no,
            "task": state["task"], "branch": state.get("branch") or "(detached)", "generated": utc(),
            "round_dir": str(round_dir), "weights": cfg["weights"], "steps": [], "shots": [], "media": {},
            "tests": None, "review": None, "tree": None, "costs": {}}


def finish_round(cfg, state, run_dir, doc, verdict, fields=None):
    """Writes the round's reports (made read-only: a round's report never changes) and records the round."""
    costs = state["costs"]
    fields = dict(fields or {})
    round_costs = fields.pop("costs", {})
    doc.update(fields)
    doc["costs"].update(round_costs)
    doc["verdict"] = verdict
    doc["generated"] = utc()
    doc["costs"].update({"builder_usd_total": costs["builder_usd_total"],
                         "critic_tokens_total": costs["critic_tokens_total"]})
    round_dir = Path(doc["round_dir"])
    report.write_round(doc, round_dir, run_dir, resolve(cfg, "runs_dir"))
    for name in ("review.json", "review.md"):
        p = round_dir / name
        os.chmod(p, stat.S_IREAD)
    state["rounds"].append({"round": doc["round"], "verdict": verdict["verdict"], "reason": verdict["reason"],
                            "detail": verdict["detail"], "overall": verdict.get("overall"), "tree": doc.get("tree"),
                            "builder_usd": doc["costs"].get("builder_usd", 0.0),
                            "critic_tokens": doc["costs"].get("critic_tokens", 0)})
    save_state(run_dir, state)
    print(f"[aicritic] round {doc['round']}: {verdict['verdict']} ({verdict['reason']}) {verdict['detail']}", flush=True)
    return verdict


def _ctx(cfg, state, run_dir, round_dir, round_no, round_start, deadline, base_tree, tree, mode, **extra):
    return {"run_id": state["run_id"], "round_no": round_no, "run_dir": run_dir, "round_dir": round_dir,
            "round_start": round_start, "deadline": deadline, "base_tree": base_tree, "tree": tree, "mode": mode,
            "task": state["task"], "costs": state["costs"], "salt": letters_salt(state["run_id"], round_no), **extra}


def _diff_files(wt, round_dir, base_tree, prev_tree, tree):
    gitops.diff(wt, base_tree, tree, round_dir / "diff.patch")
    gitops.diff(wt, prev_tree, tree, round_dir / "round-diff.patch")
    stat_text = gitops.diff_stat(wt, base_tree, tree)
    (round_dir / "diffstat.txt").write_text(stat_text + "\n", encoding="utf-8")
    return stat_text


def refuse_nested(allow_nested):
    """`run` starts `claude -p`; from inside a Claude Code session that would be Claude orchestrating Claude."""
    if os.environ.get("CLAUDECODE") and not allow_nested:
        raise SystemExit("aicritic run starts its own Claude builder, and this is already a Claude Code session. "
                         "Use /critic-loop (loop-start + loop-review) instead, or pass --allow-nested on purpose.")


# ---------------------------------------------------------------------------- run: claude -p as builder

def run(cfg, task, max_rounds=None, dry_run=False, fake_pipeline=False, scenario="improve-then-pass",
        interactive=None):
    if fake_pipeline and not dry_run:
        raise SystemExit("--fake-pipeline needs --dry-run")
    max_rounds = min(max_rounds or cfg["loop"]["max_rounds"], cfg["loop"]["max_rounds"], HARD_MAX_ROUNDS)
    scene = load_scene(cfg)
    run_id = make_run_id(task)
    run_dir = resolve(cfg, "runs_dir") / run_id
    run_dir.mkdir(parents=True)
    (run_dir / "task.md").write_text(task + "\n", encoding="utf-8")
    branch_name = cfg["git"]["branch_prefix"] + run_id
    wt = (run_dir / "fake-worktree") if fake_pipeline else (resolve(cfg, "worktree_root") / run_id)
    deadline = time.time() + cfg["loop"]["max_run_minutes"] * 60
    state = new_state(cfg, run_id, "builder-loop", task, wt, branch_name, dry_run=dry_run,
                      fake_pipeline=fake_pipeline, scenario=scenario if dry_run else None, max_rounds=max_rounds)
    save_state(run_dir, state)
    print(f"[aicritic] run {run_id}: branch {branch_name}, worktree {wt}", flush=True)

    if fake_pipeline:
        fakes.init_repo(wt, branch_name)
    else:
        gitops.add_worktree(REPO_ROOT, wt, cfg["git"]["base_ref"], branch_name)
        state["library_seed"] = pipeline.seed_library(cfg, wt, run_dir)
        print(f"[aicritic] Unity Library: {state['library_seed']}", flush=True)
    base_commit, base_tree = gitops.rev(wt), gitops.head_tree(wt)
    state["base_commit"] = base_commit
    prev_tree, session, prompt = base_tree, None, None
    stop_reason, last_verdict = None, None

    for round_no in range(1, max_rounds + 1):
        round_dir = run_dir / f"round-{round_no:02d}"
        round_dir.mkdir()
        round_start = time.time()
        doc = new_round_doc(state, round_no, round_dir, cfg)
        costs = state["costs"]

        left_usd = cfg["budget"]["builder_usd_total"] - costs["builder_usd_total"]
        if left_usd < MIN_BUILDER_USD:
            stop_reason = "builder_budget_exhausted"
            last_verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked(
                "budget_exhausted", f"only ${left_usd:.2f} of the builder budget left"))
            break
        if costs["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
            stop_reason = "critic_budget_exhausted"
            last_verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked(
                "budget_exhausted", "the critic token budget is used up"))
            break
        if time.time() > deadline:
            stop_reason = "run_time_limit"
            last_verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked(
                "time_limit", "the run reached loop.max_run_minutes"))
            break

        # ---- builder
        if prompt is None:
            template = (TOOL_DIR / "prompts" / "builder_task.md").read_text(encoding="utf-8")
            prompt = report.fill(template, task=task, worktree=wt, branch=branch_name)
        budget = min(cfg["budget"]["builder_usd_per_round"], left_usd)
        print(f"[aicritic] round {round_no}: builder (budget ${budget:.2f})", flush=True)
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
            last_verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked("builder_failed", b["error"]))
            break
        builder_tree = gitops.snapshot(wt, gitops.temp_index(wt, "builder"))
        doc["tree"] = builder_tree
        stat_text = _diff_files(wt, round_dir, base_tree, prev_tree, builder_tree)
        doc["diff_stat"] = stat_text
        problems = builder_policy_problems(cfg, wt, base_commit, branch_name, base_tree, builder_tree)
        if problems:
            stop_reason = "builder_policy_violation"
            last_verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked(
                "builder_policy_violation", "; ".join(problems)))
            break

        verdict, fields = evaluate(cfg, scene, wt, _ctx(
            cfg, state, run_dir, round_dir, round_no, round_start, deadline, base_tree, builder_tree, "change",
            settle=True, dry_run=dry_run, fake_pipeline=fake_pipeline, scenario=scenario,
            diff_file=round_dir / "diff.patch", diff_stat=stat_text))
        last_verdict = finish_round(cfg, state, run_dir, doc, verdict, fields)
        prev_tree = fields.get("tree") or builder_tree
        if verdict["verdict"] == PASS:
            stop_reason = "pass"
            break
        if verdict["verdict"] == BLOCKED:
            stop_reason = verdict["reason"] if verdict["reason"] != "insufficient_evidence" else "blocked"
            break
        if round_no == max_rounds:
            stop_reason = "max_rounds"
            break
        prompt = feedback_prompt(cfg, task, round_no + 1, max_rounds, fields["review"], verdict)

    _finish_run(cfg, state, run_dir, last_verdict, stop_reason, base_tree, dry_run)
    if interactive is None:
        interactive = sys.stdin.isatty() and not dry_run
    if interactive and state["approval"]["state"] == "pending":
        approve(cfg, run_id, None, prompt_user=True)
    return state


def _finish_run(cfg, state, run_dir, last_verdict, stop_reason, base_tree, dry_run=False, changes_possible=True):
    final_tree = state["rounds"][-1]["tree"] if state["rounds"] else None
    state.update({"status": "finished", "finished": utc(), "final_tree": final_tree,
                  "outcome": {"verdict": last_verdict["verdict"] if last_verdict else BLOCKED,
                              "stop_reason": stop_reason, "rounds": len(state["rounds"]),
                              "overall": last_verdict.get("overall") if last_verdict else None}})
    if not changes_possible:
        state["approval"] = {"state": "not_applicable"}
    elif dry_run:
        state["approval"] = {"state": "dry_run"}  # dry-run changes come from the fake builder
    else:
        has_changes = bool(final_tree) and final_tree != base_tree
        state["approval"] = {"state": "pending" if has_changes else "nothing_to_approve"}
    save_state(run_dir, state)
    print_summary(run_dir, state)


# ---------------------------------------------------------------------------- loop-start / loop-review

def loop_start(cfg, task, from_run=None, max_rounds=None):
    """A Builder/Critic run whose builder is the caller (the /critic-loop slash command)."""
    max_rounds = min(max_rounds or cfg["loop"]["max_rounds"], cfg["loop"]["max_rounds"], HARD_MAX_ROUNDS)
    run_id = make_run_id(task, "loop-")
    run_dir = resolve(cfg, "runs_dir") / run_id
    run_dir.mkdir(parents=True)
    seed_issues = []
    if from_run:
        src_dir, src = load_state(cfg, from_run)
        latest = json.loads((src_dir / "latest-review.json").read_text(encoding="utf-8"))
        seed_issues = scoring.ranked_issues(latest["review"]) if latest.get("review") else []
        task = (f"{task}\n\nVerified issues from {src.get('kind', 'run')} {from_run} "
                f"(evidence in {src_dir}):\n\n{_issue_lines(seed_issues)}")
    (run_dir / "task.md").write_text(task + "\n", encoding="utf-8")
    branch_name = cfg["git"]["branch_prefix"] + run_id
    wt = resolve(cfg, "worktree_root") / run_id
    gitops.add_worktree(REPO_ROOT, wt, cfg["git"]["base_ref"], branch_name)
    state = new_state(cfg, run_id, "external-loop", task, wt, branch_name, max_rounds=max_rounds,
                      from_run=from_run, base_commit=gitops.rev(wt), base_tree=gitops.head_tree(wt),
                      library_seed=pipeline.seed_library(cfg, wt, run_dir))
    save_state(run_dir, state)
    print(f"[aicritic] loop {run_id} started.\n  Worktree: {wt}\n  Branch:   {branch_name}\n"
          f"  Task:     {run_dir / 'task.md'}\n"
          f"Make the changes inside the worktree (never commit there), then run:\n"
          f"  python tools/AICritic/aicritic.py loop-review {run_id}")
    return state


def loop_review(cfg, run_id):
    """Evaluates the current content of an external loop's worktree as its next round."""
    run_dir, state = load_state(cfg, run_id)
    if state.get("kind") != "external-loop":
        raise SystemExit(f"{run_id} is not a /critic-loop run")
    if state["status"] == "finished":
        raise SystemExit(f"{run_id} has finished ({state['outcome']['stop_reason']}); start a new loop")
    round_no = len(state["rounds"]) + 1
    if round_no > state["max_rounds"]:
        raise SystemExit(f"{run_id} already used its {state['max_rounds']} rounds")
    wt = Path(state["worktree"])
    scene = load_scene(cfg)
    round_dir = run_dir / f"round-{round_no:02d}"
    round_dir.mkdir()
    round_start = time.time()
    deadline = round_start + cfg["loop"]["max_run_minutes"] * 60
    doc = new_round_doc(state, round_no, round_dir, cfg)
    base_tree = state["base_tree"]
    prev_tree = state["rounds"][-1]["tree"] if state["rounds"] else base_tree
    tree = gitops.snapshot(wt, gitops.temp_index(wt, "builder"))
    doc["tree"] = tree
    stat_text = _diff_files(wt, round_dir, base_tree, prev_tree, tree)
    doc["diff_stat"] = stat_text
    problems = builder_policy_problems(cfg, wt, state["base_commit"], state["branch"], base_tree, tree)
    stop_reason = None
    if problems:
        verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked("builder_policy_violation", "; ".join(problems)))
        stop_reason = "builder_policy_violation"
    elif state["costs"]["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
        verdict = finish_round(cfg, state, run_dir, doc, scoring.blocked("budget_exhausted", "the critic token budget is used up"))
        stop_reason = "critic_budget_exhausted"
    else:
        verdict, fields = evaluate(cfg, scene, wt, _ctx(
            cfg, state, run_dir, round_dir, round_no, round_start, deadline, base_tree, tree, "change",
            settle=True, diff_file=round_dir / "diff.patch", diff_stat=stat_text))
        finish_round(cfg, state, run_dir, doc, verdict, fields)
        if verdict["verdict"] == PASS:
            stop_reason = "pass"
        elif verdict["verdict"] == BLOCKED:
            stop_reason = verdict["reason"]
        elif round_no >= state["max_rounds"]:
            stop_reason = "max_rounds"
        else:
            fb = feedback_prompt(cfg, state["task"], round_no + 1, state["max_rounds"], fields["review"], verdict)
            (round_dir / "feedback.md").write_text(fb, encoding="utf-8")
            print(f"\n[aicritic] {verdict['verdict']}: fix the issues in {round_dir / 'feedback.md'} inside {wt}, "
                  f"then run loop-review {run_id} again ({state['max_rounds'] - round_no} round(s) left).")
    if stop_reason:
        _finish_run(cfg, state, run_dir, verdict, stop_reason, base_tree)
    return state


# ---------------------------------------------------------------------------- summary, approve, discard

def print_summary(run_dir, state):
    o = state["outcome"]
    print("")
    print(f"[aicritic] {state.get('kind', 'run')} {state['run_id']} finished: {o['verdict']} after {o['rounds']} "
          f"round(s), stop reason {o['stop_reason']}, score {o['overall'] if o['overall'] is not None else '–'}")
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
    # Compare trees, not the working copy: `git diff <commit>` would leave out the files the builder added.
    print(gitops.diff_stat(wt, state["base_commit"], state["final_tree"]) or "(no changes)")
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
               f"Approved manually; the reviews are in tools/AICritic/runs/{run_id}/.\n\n"
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
        gitops.remove_worktree(REPO_ROOT, wt, resolve(cfg, "worktree_root"))
    gitops.git(["worktree", "prune"], REPO_ROOT)
    if state.get("branch") and gitops.branch_exists(REPO_ROOT, state["branch"]):
        gitops.delete_branch(REPO_ROOT, state["branch"])
    state["approval"] = {"state": "discarded", "at": utc()}
    save_state(run_dir, state)
    print(f"[aicritic] removed worktree {wt}{' and branch ' + state['branch'] if state.get('branch') else ''}; "
          f"the reports stay in {run_dir}")
    return True
