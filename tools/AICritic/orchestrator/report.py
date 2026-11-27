"""Review reports: review.json/review.md per round, latest-review.json/.md per run and globally."""

import json
import os
import shutil
from pathlib import Path

from . import CRITERIA, CRITERION_TITLES
from .scoring import ranked_issues


def fill(template, **values):
    """Replaces {key} placeholders without interpreting other braces (tasks and logs contain them)."""
    for key, value in values.items():
        template = template.replace("{" + key + "}", str(value))
    return template


def _fmt(v, digits=1):
    return "–" if v is None else (f"{v:.{digits}f}" if isinstance(v, float) else str(v))


def _link(target, base_dir):
    return Path(os.path.relpath(target, base_dir)).as_posix()


def markdown(review_doc, base_dir):
    """review_doc is the round's review.json content. Links are relative to base_dir."""
    v = review_doc["verdict"]
    lines = [f"# AI critic review: {review_doc['run_id']}, round {review_doc['round']}", ""]
    lines += [f"**Verdict: {v['verdict']}** ({v['reason']}). {v['detail']}", ""]
    if v.get("overall") is not None:
        lines.append(f"Weighted score: **{v['overall']:.2f}/10** (pass needs {v['pass_score']}).")
    elif v.get("indicative_partial_score") is not None:
        lines.append(f"No overall score. Indicative score of the evaluable criteria only: "
                     f"{v['indicative_partial_score']:.2f}/10 ({v['coverage_percent']:.0f}% of the weight).")
    lines += ["", f"Task: {review_doc['task']}", "",
              f"Branch `{review_doc['branch']}`, build of tree `{review_doc.get('tree') or '–'}`, "
              f"generated {review_doc['generated']}.", ""]

    review = review_doc.get("review")
    if review:
        lines += ["## Scores", "", "| Criterion | Weight | Score | Confidence | Rationale |", "|---|---|---|---|---|"]
        for n in CRITERIA:
            c = review["criteria"][n]
            score = _fmt(c["score"]) if c["evaluable"] else "**null** (not evaluable)"
            why = c["rationale"] if c["evaluable"] else f"{c['rationale']} Missing: {c['missing_evidence']}"
            lines.append(f"| {CRITERION_TITLES[n]} | {review_doc['weights'][n]}% | {score} | {c['confidence']} | "
                         f"{why.replace('|', '/').replace(chr(10), ' ')} |")
        ta = review["task_assessment"]
        lines += ["", f"**Summary.** {review['summary']}", "",
                  f"**Task completed:** {ta['completed']}. {ta['explanation']}", "", "## Issues (ranked)", ""]
        issues = ranked_issues(review)
        if not issues:
            lines += ["None reported.", ""]
        for i in issues:
            scope = "in scope" if i["in_scope"] else "outside the task"
            lines += [f"{i['rank']}. **{i['title']}** ({i['severity']}, {CRITERION_TITLES[i['criterion']]}, {scope})",
                      f"   {i['detail']}",
                      f"   Fix: {i['suggested_fix']}",
                      f"   Evidence: {', '.join(i['evidence']) or '–'}", ""]
        if review_doc.get("review_warnings"):
            lines += ["Review warnings: " + "; ".join(review_doc["review_warnings"]), ""]

    lines += ["## Pipeline", "", "| Step | Result | Time | Log |", "|---|---|---|---|"]
    for s in review_doc.get("steps", []):
        result = "ok" if s.get("ok") else f"FAILED: {s.get('error') or 'exit ' + str(s.get('returncode'))}"
        log = s.get("log")
        lines.append(f"| {s['name']} | {result} | {_fmt(s.get('seconds'))} s | "
                     f"{'[log](' + _link(log, base_dir) + ')' if log else '–'} |")
    tests = review_doc.get("tests")
    if tests:
        lines += ["", f"Tests: {tests['passed']} passed, {tests['failed']} failed, {tests['skipped']} skipped "
                      f"of {tests['total']}."]

    shots = review_doc.get("shots") or []
    if shots:
        lines += ["", "## Screenshots (this round's build)", ""]
        shots_dir = Path(review_doc["round_dir"]) / "evidence" / "screenshots"
        for s in shots:
            m = (review_doc.get("media") or {}).get(s["shot"], {})
            pic = m.get("gif") or m.get("sheet") or (m.get("frames") or [None])[0]
            met = s.get("metrics") or {}
            lines.append(f"**{s['shot']}**: {'ok' if s['ok'] else 'FAILED: ' + str(s.get('error'))}. "
                         f"Peak memory {_fmt(met.get('peak_working_set_mb'))} MB, "
                         f"CPU {_fmt(met.get('avg_cpu_percent_of_machine'))}% of the machine.")
            if pic:
                lines += ["", f"![{s['shot']}]({_link(shots_dir / pic, base_dir)})", ""]

    exploration = review_doc.get("exploration")
    if exploration:
        evidence_dir = Path(review_doc["round_dir"]) / "evidence" / "explore"
        lines += ["", "## Exploration", "", "Every row below ran in the game client and returned its own observation "
                  "and screenshot (rejected actions never reached the client).", ""]
        for name, e in exploration.items():
            lines += [f"### {name}: {e.get('mode')}, {e.get('class')} {e.get('character')}", ""]
            if e.get("failure"):
                lines += [f"Stopped early: {e['failure']}", ""]
            lines += ["| Step | Source | Action | Result | Picture |", "|---|---|---|---|---|"]
            for s in e.get("steps", []):
                params = ", ".join(f"{k}={v}" for k, v in (s.get("params") or {}).items())
                if s.get("rejected"):
                    result = "REJECTED: " + s["rejected"]
                else:
                    result = ("ok: " if s.get("ok") else "FAILED: ") + (s.get("result") or "")
                pic = s.get("screenshot")
                link = f"[{Path(pic).name}]({_link(evidence_dir / name / pic, base_dir)})" if pic else "–"
                lines.append(f"| {s['step']} | {s.get('source')} | `{s['action']}` {params} | "
                             f"{result.replace('|', '/')} | {link} |")
            lines.append("")
        findings = review_doc.get("explorer_findings") or []
        if findings:
            lines += ["Explorer's notes while playing (leads, not verified issues):", ""]
            lines += [f"- [{f['severity']}] {f['scenario']} step {f['step']}: {f['title']}. {f['detail']}" for f in findings]
            lines.append("")

    costs = review_doc.get("costs") or {}
    lines += ["", "## Cost", "",
              f"Builder this round: ${costs.get('builder_usd', 0):.2f}; run total ${costs.get('builder_usd_total', 0):.2f}. "
              f"Critic this round: {costs.get('critic_tokens', 0):,} tokens; run total {costs.get('critic_tokens_total', 0):,}.",
              ""]
    return "\n".join(lines)


def write_round(review_doc, round_dir, run_dir, runs_dir):
    """Writes the round's review.json/.md and refreshes the latest-review copies of the run and of all runs."""
    round_dir, run_dir, runs_dir = Path(round_dir), Path(run_dir), Path(runs_dir)
    data = json.dumps(review_doc, indent=2)
    for folder in (round_dir, run_dir, runs_dir):
        name = "review" if folder == round_dir else "latest-review"
        (folder / f"{name}.json").write_text(data, encoding="utf-8")
        (folder / f"{name}.md").write_text(markdown(review_doc, folder), encoding="utf-8")


def copy_if_exists(src, dst):
    if Path(src).exists():
        shutil.copy2(src, dst)
