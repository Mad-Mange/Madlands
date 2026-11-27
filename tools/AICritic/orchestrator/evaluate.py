"""One evaluation of a worktree: build and test, play the scene, collect evidence, ask the critic, score.
Shared by the Builder/Critic loop (`run`, `loop-review`), `review` and `audit`."""

import time
from pathlib import Path

from . import capture, critic, evidence, fakes, gitops, pipeline, report, scoring
from .config import TOOL_DIR, resolve

PROMPTS = {"change": "critic.md", "audit": "critic_audit.md"}


def critic_prompt(cfg, scene, mode, task, manifest, images, diff_stat):
    shots = "\n".join(f"- `{s['name']}`{' (burst of ' + str(s['burst']) + ')' if int(s.get('burst', 1)) > 1 else ''}: "
                      f"{s.get('description', '')}" for s in scene["shot"])
    image_list = "\n".join(f"{i + 1}. `screenshots/{name}`: {desc}" for i, (name, desc) in enumerate(images))
    template = (TOOL_DIR / "prompts" / PROMPTS[mode]).read_text(encoding="utf-8")
    stat_line = diff_stat.strip().splitlines()[-1].strip() if diff_stat.strip() else "no changes"
    return report.fill(template, task=task, build_finished=manifest.get("build", {}).get("finished"),
                       base_ref=cfg["git"]["base_ref"], diff_stat_line=stat_line, scene_name=scene["name"],
                       shot_list=shots, image_list=image_list, facts=facts_text(manifest),
                       pass_score=cfg["loop"]["pass_score"])


def facts_text(manifest):
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
    return "\n".join(facts)


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


def evaluate(cfg, scene, wt, ctx):
    """Evaluates the worktree as it is. ctx: run_id, round_no, run_dir, round_dir, round_start, deadline,
    base_tree, tree (the content under review), mode ('change' or 'audit'), task, costs (updated in place),
    salt, settle (restore Unity's side effects), dry_run, fake_pipeline, scenario, diff_file.
    Returns (verdict, fields for the round report)."""
    round_dir, run_dir = Path(ctx["round_dir"]), Path(ctx["run_dir"])
    costs, round_no = ctx["costs"], ctx["round_no"]
    fake, dry = ctx.get("fake_pipeline", False), ctx.get("dry_run", False)
    fields = {"steps": [], "shots": [], "media": {}, "tests": None, "review": None, "tree": ctx["tree"],
              "costs": {}}

    def blocked(reason, detail):
        return scoring.blocked(reason, detail), fields

    # ---- build and tests
    print(f"[aicritic] round {round_no}: build and tests", flush=True)
    if fake:
        ok, steps, failed = fakes.pipeline(round_dir, round_no, ctx.get("scenario"), wt)
    else:
        ok, steps, failed = pipeline.run_pipeline(cfg, wt, round_dir, ctx["round_start"])
    fields["steps"] = steps
    fields["tests"] = next((s.get("tests") for s in steps if s["name"] == "dotnet_test"), None)
    if not ok:
        step = next(s for s in steps if s["name"] == failed)
        return blocked("required_step_failed", f"{failed}: {step.get('error') or 'failed'}")
    exe = pipeline.client_exe(wt)
    build_finished = exe.stat().st_mtime if exe.exists() else None

    # ---- play session
    print(f"[aicritic] round {round_no}: play session ({len(scene['shot'])} shots)", flush=True)
    if fake:
        ok, cap, failure = fakes.capture(scene, round_dir)
    else:
        ok, cap, failure = capture.run_capture(cfg, scene, wt, round_dir, ctx["salt"])
    fields["shots"] = cap["shots"]
    if not ok:
        return blocked("capture_failed", failure)

    # ---- Unity's side effects, then the evidence
    tree = ctx["tree"]
    if ctx.get("settle") and not fake:
        tree, kept, reverted = settle_side_effects(wt, ctx["base_tree"], ctx["tree"])
        fields.update({"tree": tree, "pipeline_kept": kept, "pipeline_reverted": reverted})
    media = evidence.prepare_media(scene, round_dir)
    fields["media"] = media
    manifest = evidence.build_manifest(round_dir, {
        "run_id": ctx["run_id"], "round": round_no, "mode": ctx["mode"], "tree": tree,
        "build": {"client_exe": str(exe), "client_sha256": evidence.sha256(exe) if exe.exists() else None,
                  "finished": evidence.iso(build_finished) if build_finished else None},
        "steps": steps, "tests": fields["tests"], "capture": cap, "media": media})
    stale = evidence.freshness_problems(manifest, ctx["round_start"], build_finished)
    if stale:
        return blocked("stale_evidence", "; ".join(stale[:5]))

    # ---- critic
    if costs["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
        return blocked("budget_exhausted", "the critic token budget is used up")
    stage_dir = resolve(cfg, "worktree_root") / "_critic" / ctx["run_id"] / f"round-{round_no:02d}"
    extra = {"task.md": run_dir / "task.md"}
    if ctx.get("diff_file") and Path(ctx["diff_file"]).exists():
        extra["diff.patch"] = ctx["diff_file"]
    stage = evidence.stage_for_critic(round_dir, stage_dir, extra)
    images = evidence.critic_images(scene, media, cfg["critic"]["max_images"])
    diff_stat = ctx.get("diff_stat", "")
    cprompt = critic_prompt(cfg, scene, ctx["mode"], ctx["task"], manifest, images, diff_stat)
    before_wt, before_stage = gitops.state_fingerprint(wt), evidence.folder_fingerprint(stage)
    print(f"[aicritic] round {round_no}: critic ({len(images)} images)", flush=True)
    if dry:
        c = fakes.critic(round_no, ctx.get("scenario"), round_dir / "critic", cprompt, wt)
    else:
        timeout = min(cfg["timeouts"]["critic"], max(60, ctx["deadline"] - time.time()))
        c = critic.run(cfg, stage, round_dir / "critic", cprompt,
                       [stage / "screenshots" / name for name, _ in images], timeout)
    tokens = int(c["usage"].get("total_tokens") or 0)
    costs["critic_tokens_total"] += tokens
    fields["costs"]["critic_tokens"] = tokens
    fields["critic"] = {k: v for k, v in c.items() if k != "review"}
    after_wt, after_stage = gitops.state_fingerprint(wt), evidence.folder_fingerprint(stage)
    if after_wt["hash"] != before_wt["hash"] or after_stage != before_stage:
        what = "the worktree" if after_wt["hash"] != before_wt["hash"] else "its evidence folder"
        return blocked("critic_integrity_violation", f"files changed in {what} while the critic ran; the review is discarded")
    if not c["ok"]:
        return blocked("critic_failed", c["error"])
    try:
        review, warnings = scoring.normalize_review(c["review"], cfg["loop"]["pass_score"])
    except scoring.ReviewError as e:
        return blocked("critic_invalid_review", str(e))
    fields.update({"review": review, "review_warnings": warnings})
    return scoring.score(review, cfg["weights"], cfg["loop"]["pass_score"]), fields
