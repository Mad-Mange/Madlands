"""/critic-explore: Codex plays the game through the Playtest Controller.

  build      a detached worktree (HEAD, a ref, or a snapshot of uncommitted work), dotnet build and tests,
             the content validation and the playtest client (Editor/PlaytestBuild: MADLANDS_PLAYTEST)
  realm      a fresh in-memory realm with --gm-all and a fixed seed; a new test character per scenario
  loop       observation -> Codex explorer picks one allowlisted action -> validate -> the client executes
             it -> new observation with a screenshot (or the scenario's script in scripted mode)
  review     a final Codex review of everything that happened, scored like every other review

An action counts as executed only when the client's own observation and screenshot came back.
"""

import json
import shutil
import time
from pathlib import Path

from . import CRITERIA, capture, critic, evidence, fakes, gitops, pipeline, playtest, proc, scoring
from .config import REPO_ROOT, TOOL_DIR, resolve
from .loop import _finish_run, finish_round, letters_salt, make_run_id, new_round_doc, new_state, save_state
from .report import fill
from .review import _prepare

# gm_tools drives the developer GM menu's commands (cheats; always scripted, never part of "all").
SCENARIO_NAMES = ("smoke", "world", "combat", "ui", "navigation", "gm_tools")
ALL_SCENARIOS = ("world", "combat", "ui", "navigation")
MAX_HISTORY = 10


def expand(names):
    out = []
    for n in names or ["world"]:
        for m in (ALL_SCENARIOS if n == "all" else (n,)):
            if m not in SCENARIO_NAMES:
                raise SystemExit(f"unknown scenario '{m}' (known: {', '.join(SCENARIO_NAMES)}, all)")
            if m not in out:
                out.append(m)
    return out


def explorer_schema():
    """The explorer's answer: one allowlisted action with every parameter present (null when unused)."""
    params = {}
    for spec in playtest.ACTIONS.values():
        for key, (kind, *_rest) in spec.items():
            t = {"num": "number", "int": "integer", "bool": "boolean"}.get(kind, "string")
            params[key] = {"type": [t, "null"]}
    params = dict(sorted(params.items()))
    return {
        "type": "object", "additionalProperties": False,
        "required": ["observation_notes", "action", "params", "finding", "done"],
        "properties": {
            "observation_notes": {"type": "string"},
            "action": {"type": "string", "enum": list(playtest.ACTIONS)},
            "params": {"type": "object", "additionalProperties": False, "required": list(params), "properties": params},
            "finding": {"type": "object", "additionalProperties": False,
                        "required": ["noticed", "severity", "criterion", "title", "detail"],
                        "properties": {"noticed": {"type": "boolean"},
                                       "severity": {"type": "string", "enum": ["critical", "major", "minor", "none"]},
                                       "criterion": {"type": "string", "enum": list(CRITERIA) + ["none"]},
                                       "title": {"type": "string"}, "detail": {"type": "string"}}},
            "done": {"type": "boolean"},
        },
    }


def compact_observation(obs):
    keep = ("ok", "result", "in_world", "player", "has_target", "target", "nearby", "loot_open", "dialog_open",
            "stuck", "moved", "fps_avg", "fps_min", "events", "log_error_count", "log_errors")
    return {k: obs.get(k) for k in keep if k in obs}


def history_line(step):
    params = ", ".join(f"{k}={v}" for k, v in step["params"].items())
    status = "REJECTED: " + step["rejected"] if step.get("rejected") else ("ok" if step["ok"] else "failed")
    return f"{step['step']}. {step['action']}({params}) -> {status}: {step.get('result', '')}"


class _Explorer:
    """One Codex call per step, with a fresh evidence folder outside every repository."""

    def __init__(self, cfg, run_id, scenario, out_dir, deadline, costs):
        self.cfg, self.scenario, self.out_dir, self.deadline, self.costs = cfg, scenario, Path(out_dir), deadline, costs
        self.stage_root = resolve(cfg, "worktree_root") / "_critic" / run_id / "explore" / scenario["name"]
        self.schema = self.out_dir / "explorer.schema.json"
        self.out_dir.mkdir(parents=True, exist_ok=True)
        self.schema.write_text(json.dumps(explorer_schema(), indent=2), encoding="utf-8")
        self.template = (TOOL_DIR / "prompts" / "explorer_step.md").read_text(encoding="utf-8")

    def decide(self, step_no, max_actions, steps, findings, obs, screenshot):
        stage = self.stage_root / f"step-{step_no:02d}"
        if stage.exists():
            shutil.rmtree(stage)
        stage.mkdir(parents=True)
        shot = stage / "screenshot.png"
        shutil.copy2(screenshot, shot)
        observation = json.dumps(compact_observation(obs), indent=1)
        (stage / "observation.json").write_text(observation, encoding="utf-8")
        prompt = fill(self.template, scenario=self.scenario["name"], goal=self.scenario.get("goal", ""),
                      step=step_no, max_actions=max_actions,
                      minutes_left=max(0, round((self.deadline - time.time()) / 60)),
                      allowlist=playtest.allowlist_text(self.scenario["locations"]),
                      history="\n".join(history_line(s) for s in steps[-MAX_HISTORY:]) or "(none yet)",
                      findings="\n".join(f"- [{f['severity']}] {f['title']}" for f in findings) or "(none yet)",
                      observation=observation)
        timeout = min(self.cfg["timeouts"]["explorer_step"], max(30, self.deadline - time.time()))
        r = critic.run(self.cfg, stage, self.out_dir / f"step-{step_no:02d}", prompt, [shot], timeout,
                       schema=self.schema, effort=self.cfg["explore"]["explorer_effort"])
        self.costs["critic_tokens_total"] += int(r["usage"].get("total_tokens") or 0)
        return r


def _scripted_decisions(scenario):
    for step in scenario.get("script", []):
        step = dict(step)
        name = step.pop("action")
        yield {"observation_notes": "scripted", "action": name, "params": step,
               "finding": {"noticed": False, "severity": "none", "criterion": "none", "title": "", "detail": ""},
               "done": False}


def run_scenario(cfg, run_id, scenario, session, mode, max_actions, deadline, costs, ex_dir, dry_run):
    """Plays one scenario on a running client. Returns (steps, findings, failure or None)."""
    steps, findings = [], []
    log = open(ex_dir / "steps.jsonl", "w", encoding="utf-8")

    def record(step):
        steps.append(step)
        log.write(json.dumps(step) + "\n")
        log.flush()

    def execute(name, params):
        obs = None
        for cmd in playtest.to_commands(name, params, scenario):
            obs = session.send(cmd)
            if not obs.get("ok") and name != "reset":
                break
        return obs

    try:
        # Setup (trusted: the scenario file): GM lines, then the start location.
        for i, line in enumerate(scenario.get("setup", [])):
            obs = session.send({"action": "gm", "text": line})
            record({"step": f"setup-{i + 1}", "source": "setup", "action": "gm", "params": {"text": line},
                    "ok": obs.get("ok"), "result": obs.get("result"), "screenshot": obs.get("screenshot"),
                    "seq": obs.get("seq")})
        obs = execute("goto", {"location": scenario["start"]})
        record({"step": "setup-start", "source": "setup", "action": "goto", "params": {"location": scenario["start"]},
                "ok": obs.get("ok"), "result": obs.get("result"), "screenshot": obs.get("screenshot"), "seq": obs.get("seq")})

        scripted = mode == "scripted" or dry_run or scenario.get("scripted_only", False)
        explorer = None if scripted else _Explorer(cfg, run_id, scenario, ex_dir / "codex", deadline, costs)
        script = _scripted_decisions(scenario)
        for step_no in range(1, max_actions + 1):
            if time.time() > deadline:
                return steps, findings, None
            if explorer is None:
                decision = next(script, None)
                if decision is None:
                    break
                source = "script"
            else:
                if costs["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
                    return steps, findings, "the critic token budget is used up"
                r = explorer.decide(step_no, max_actions, steps, findings, obs,
                                    session.mailbox / obs["screenshot"])
                if not r["ok"]:
                    return steps, findings, f"explorer call failed at step {step_no}: {r['error']}"
                decision, source = r["review"], "codex"
            f = decision.get("finding") or {}
            if f.get("noticed"):
                findings.append({"scenario": scenario["name"], "step": step_no, "severity": f.get("severity"),
                                 "criterion": f.get("criterion"), "title": f.get("title"), "detail": f.get("detail"),
                                 "screenshot": f"explore/{scenario['name']}/{obs.get('screenshot')}"})
            if decision.get("done"):
                record({"step": step_no, "source": source, "action": "done", "params": {}, "ok": True,
                        "result": "the explorer considered the goal covered", "notes": decision.get("observation_notes")})
                break
            name = decision["action"]
            try:
                params = playtest.validate_action(name, decision.get("params"), scenario["locations"], script=source == "script")
            except playtest.ActionError as e:
                record({"step": step_no, "source": source, "action": name, "params": decision.get("params") or {},
                        "ok": False, "rejected": str(e), "result": "", "notes": decision.get("observation_notes")})
                continue
            obs = execute(name, params)
            record({"step": step_no, "source": source, "action": name, "params": params, "ok": obs.get("ok"),
                    "result": obs.get("result"), "screenshot": obs.get("screenshot"), "seq": obs.get("seq"),
                    "frames": obs.get("frames", []),
                    "moved": obs.get("moved"), "stuck": obs.get("stuck"), "fps_avg": obs.get("fps_avg"),
                    "fps_p5": obs.get("fps_p5"), "fps_min": obs.get("fps_min"), "events": obs.get("events", [])[:12],
                    "log_errors": obs.get("log_errors", []), "notes": decision.get("observation_notes"),
                    **({"gm": obs.get("gm_result")} if obs.get("has_gm_result") else {})})
        return steps, findings, None
    except playtest.ClientGone as e:
        return steps, findings, str(e)
    finally:
        log.close()


def _metrics(sampler, client):
    if sampler is None or client is None:
        return {}
    try:
        return sampler.finish(client.proc, max(1e-3, time.monotonic() - client.started))
    except Exception:  # metrics are a nice-to-have; never fail a run on them
        return {}


def explore(cfg, scenarios, mode="ai", max_actions=None, max_minutes=None, ref="HEAD", snapshot=False, paths=None,
            keep_worktree=False, dry_run=False, fake_client=False, final_review=True, reuse_build=None):
    """reuse_build: an existing worktree whose playtest client is played as it is (no new build). The
    pictures must still be newer than that executable, and the report says the build was reused."""
    names = expand(scenarios)
    loaded = [playtest.load_scenario(n) for n in names]
    if dry_run:
        mode = "scripted"
    ecfg = cfg["explore"]
    max_minutes = max_minutes or ecfg["max_minutes"]
    run_id = make_run_id("-".join(names), "explore-")
    run_dir = resolve(cfg, "runs_dir") / run_id
    round_dir = run_dir / "round-01"
    ev = round_dir / "evidence"
    logs = ev / "logs"
    logs.mkdir(parents=True)
    task = (f"AI exploration ({mode}) of the scenarios: {', '.join(names)}. "
            + " ".join(f"{s['name']}: {s['description']}" for s in loaded))
    (run_dir / "task.md").write_text(task + "\n", encoding="utf-8")
    wt = Path(reuse_build).resolve() if reuse_build else resolve(cfg, "worktree_root") / run_id
    if reuse_build and not pipeline.client_exe(wt, "playtest").exists():
        raise SystemExit(f"no playtest client in {wt} (builds/playtest/Madlands.exe)")
    state = new_state(cfg, run_id, "explore", task, wt if not fake_client else "(fake client)", scenarios=names,
                      mode=mode, dry_run=dry_run, fake_client=fake_client, max_rounds=1)
    save_state(run_dir, state)
    round_start = time.time()
    deadline = round_start + max_minutes * 60
    doc = new_round_doc(state, 1, round_dir, cfg)
    print(f"[aicritic] explore {run_id}: {', '.join(names)} ({mode}{', dry run' if dry_run else ''})", flush=True)

    def blocked(reason, detail, fields=None):
        v = finish_round(cfg, state, run_dir, doc, scoring.blocked(reason, detail), fields)
        _finish_run(cfg, state, run_dir, v, reason, None, dry_run, changes_possible=False)
        _cleanup()
        return state

    def _cleanup():
        if not fake_client and not keep_worktree and not reuse_build and wt.exists():
            gitops.remove_worktree(REPO_ROOT, wt, resolve(cfg, "worktree_root"))
            state["worktree_removed"] = True
            save_state(run_dir, state)

    # ---- build
    steps, exe, build_finished = [], None, None
    if reuse_build:
        exe = pipeline.client_exe(wt, "playtest")
        build_finished = exe.stat().st_mtime
        round_start = min(round_start, build_finished)
        steps = [{"name": "reused_build", "ok": True, "returncode": 0, "seconds": 0.0, "log": None,
                  "error": "", "detail": f"played the existing playtest client of {wt}, built "
                                         f"{evidence.iso(build_finished)} at commit {gitops.rev(wt)}"}]
        state.update({"reviewed": f"reused build in {wt}", "commit": gitops.rev(wt)})
        save_state(run_dir, state)
    elif not fake_client:
        commit, what = _prepare(cfg, run_dir, wt, ref, snapshot, paths, False)
        state.update({"reviewed": what, "commit": commit, "library_seed": pipeline.seed_library(cfg, wt, run_dir)})
        save_state(run_dir, state)
        print(f"[aicritic] building {what}", flush=True)
        ok, steps, failed = pipeline.run_pipeline(cfg, wt, round_dir, round_start, target="playtest",
                                                  tests=ecfg["run_tests"])
        doc["steps"] = steps
        doc["tests"] = next((s.get("tests") for s in steps if s["name"] == "dotnet_test"), None)
        if not ok:
            step = next(s for s in steps if s["name"] == failed)
            return blocked("required_step_failed", f"{failed}: {step.get('error') or 'failed'}", {"steps": steps})
        exe = pipeline.client_exe(wt, "playtest")
        build_finished = exe.stat().st_mtime

    # ---- realm and scenarios
    server = None
    if not fake_client:
        try:
            server = capture.start_realm(cfg, wt, logs)
        except RuntimeError as e:
            return blocked("required_step_failed", f"realm: {e}", {"steps": steps})
    results, all_findings, exploration = [], [], {}
    try:
        for i, scenario in enumerate(loaded):
            if time.time() > deadline:
                results.append({"shot": scenario["name"], "ok": False, "error": "time limit reached before it started"})
                continue
            ex_dir = ev / "explore" / scenario["name"]
            ex_dir.mkdir(parents=True)
            limit = min(max_actions or scenario.get("max_actions", 12), playtest.HARD_MAX_ACTIONS)
            char = ("Pt" + letters_salt(run_id, i + 1) + "".join(c for c in scenario["name"] if c.isalpha()))[:16].capitalize()
            print(f"[aicritic] scenario {scenario['name']}: character {char}, up to {limit} actions", flush=True)
            client, sampler, fake = None, None, None
            if fake_client:
                fake = playtest.FakeClient(ex_dir).start()
                alive = fake.alive
            else:
                client = playtest.launch_client(exe, ex_dir, logs / f"client-{scenario['name']}.log", cfg["realm"], char,
                                                scenario.get("class"), ecfg["resolution"])
                client.started = time.monotonic()
                sampler = proc._Sampler() if proc.IS_WINDOWS else None
                alive = client.alive
            session = playtest.Session(ex_dir, alive)
            failure = None
            try:
                try:
                    session.wait_ready(ecfg["ready_timeout"])
                except playtest.ClientGone as e:
                    if fake_client:
                        raise
                    # One more launch when the client never got into the world (a socket refused by the OS
                    # right after a bot run has been seen): the first attempt's log is kept.
                    client.stop()
                    log = logs / f"client-{scenario['name']}.log"
                    if log.exists():
                        log.replace(logs / f"client-{scenario['name']}.attempt1.log")
                    shutil.rmtree(ex_dir / "commands", ignore_errors=True)
                    shutil.rmtree(ex_dir / "observations", ignore_errors=True)
                    (ex_dir / "status.json").unlink(missing_ok=True)
                    print(f"[aicritic] scenario {scenario['name']}: the client did not get into the world ({e}); one retry",
                          flush=True)
                    time.sleep(5)
                    client = playtest.launch_client(exe, ex_dir, log, cfg["realm"], char + "r" if len(char) < 16 else char[:15] + "r",
                                                    scenario.get("class"), ecfg["resolution"])
                    client.started = time.monotonic()
                    alive = client.alive
                    session = playtest.Session(ex_dir, alive)
                    session.wait_ready(ecfg["ready_timeout"])
                steps_run, findings, failure = run_scenario(cfg, run_id, scenario, session, mode, limit, deadline,
                                                            state["costs"], ex_dir, dry_run)
                try:
                    session.send({"action": "quit"})
                except playtest.ClientGone:
                    pass
            except playtest.ClientGone as e:
                steps_run, findings, failure = [], [], str(e)
            finally:
                if sampler and client:
                    sampler.sample(client.proc)
                metrics = _metrics(sampler, client)
                if client:
                    deadline_exit = time.monotonic() + 10
                    while client.alive() and time.monotonic() < deadline_exit:
                        time.sleep(0.2)
                    client.stop()
                if fake:
                    fake.stop()
            executed = [s for s in steps_run if s.get("seq") and s.get("source") != "setup"]
            exploration[scenario["name"]] = {"character": char, "class": scenario.get("class"),
                                             "mode": "scripted" if scenario.get("scripted_only") else mode,
                                             "steps": steps_run, "failure": failure}
            all_findings += findings
            results.append({"shot": scenario["name"], "name": f"explore_{scenario['name']}", "ok": failure is None and bool(executed),
                            "error": failure or ("" if executed else "no action was executed"),
                            "actions_executed": len(executed), "actions_failed": sum(1 for s in executed if not s["ok"]),
                            "actions_rejected": sum(1 for s in steps_run if s.get("rejected")),
                            "metrics": metrics,
                            "client_log": capture.client_log_summary(logs / f"client-{scenario['name']}.log") if client else {},
                            "seconds": None})
            print(f"[aicritic] scenario {scenario['name']}: {len(executed)} action(s) executed"
                  + (f", stopped: {failure}" if failure else ""), flush=True)
    finally:
        if server:
            server.stop()

    # ---- evidence
    (ev / "findings.json").write_text(json.dumps(all_findings, indent=2), encoding="utf-8")
    media = _media(ev, names)
    doc.update({"steps": steps, "shots": results, "media": media, "exploration": exploration,
                "explorer_findings": all_findings})
    server_summary = capture.server_log_summary(logs / "server.log") if not fake_client else {}
    manifest = evidence.build_manifest(round_dir, {
        "run_id": run_id, "round": 1, "mode": "explore", "commit": state.get("commit"),
        "build": {"client_exe": str(exe) if exe else None, "client_sha256": evidence.sha256(exe) if exe else None,
                  "finished": evidence.iso(build_finished) if build_finished else None},
        "steps": steps, "tests": doc.get("tests"), "capture": {"shots": results, "server": server_summary},
        "exploration": {k: {kk: vv for kk, vv in v.items() if kk != "steps"} for k, v in exploration.items()},
        "media": media, "dry_run": dry_run, "fake_client": fake_client})
    if not fake_client:
        stale = evidence.freshness_problems(manifest, round_start, build_finished)
        if stale:
            return blocked("stale_evidence", "; ".join(stale[:5]), doc_fields(doc))
    failed = [r for r in results if not r["ok"]]
    if len(failed) == len(results):
        return blocked("client_failed", "; ".join(f"{r['shot']}: {r['error']}" for r in failed), doc_fields(doc))
    if not final_review:
        return blocked("not_reviewed", "the final review was skipped (--no-review)", doc_fields(doc))

    # ---- final review
    stage = resolve(cfg, "worktree_root") / "_critic" / run_id / "review"
    stage = evidence.stage_for_critic(round_dir, stage, {"task.md": run_dir / "task.md"})
    images = _review_images(names, media, exploration, cfg["critic"]["max_images"])
    prompt = fill((TOOL_DIR / "prompts" / "critic_explore.md").read_text(encoding="utf-8"), task=task,
                  build_finished=evidence.iso(build_finished) if build_finished else "(fake client)",
                  scenarios=_scenario_text(loaded, exploration, results),
                  image_list="\n".join(f"{i + 1}. `{p}`: {d}" for i, (p, d) in enumerate(images)),
                  facts=_facts(results, exploration, server_summary, doc.get("tests"), all_findings))
    print(f"[aicritic] final review ({len(images)} images)", flush=True)
    if dry_run:
        c = fakes.critic(1, "pass", round_dir / "critic", prompt, None)
    else:
        if state["costs"]["critic_tokens_total"] >= cfg["budget"]["critic_tokens_total"]:
            return blocked("budget_exhausted", "the critic token budget is used up", doc_fields(doc))
        before = evidence.folder_fingerprint(stage)
        c = critic.run(cfg, stage, round_dir / "critic", prompt, [stage / p for p, _ in images],
                       cfg["timeouts"]["critic"])
        state["costs"]["critic_tokens_total"] += int(c["usage"].get("total_tokens") or 0)
        if evidence.folder_fingerprint(stage) != before:
            return blocked("critic_integrity_violation", "files changed in the evidence folder while the critic ran",
                           doc_fields(doc))
    doc["critic"] = {k: v for k, v in c.items() if k != "review"}
    if not c["ok"]:
        return blocked("critic_failed", c["error"], doc_fields(doc))
    try:
        review, warnings = scoring.normalize_review(c["review"], cfg["loop"]["pass_score"])
    except scoring.ReviewError as e:
        return blocked("critic_invalid_review", str(e), doc_fields(doc))
    verdict = scoring.score(review, cfg["weights"], cfg["loop"]["pass_score"])
    v = finish_round(cfg, state, run_dir, doc, verdict, {"review": review, "review_warnings": warnings,
                                                          "costs": {"critic_tokens": state["costs"]["critic_tokens_total"]}})
    _finish_run(cfg, state, run_dir, v, "explored", None, dry_run, changes_possible=False)
    _cleanup()
    return state


def doc_fields(doc):
    return {k: doc[k] for k in ("steps", "shots", "media", "exploration", "explorer_findings") if k in doc}


def _media(ev, names):
    shots_dir = ev / "screenshots"
    shots_dir.mkdir(exist_ok=True)
    media = {}
    for name in names:
        frames = sorted((ev / "explore" / name / "screenshots").glob("*.png"))
        if len(frames) > 16:  # keep the sheet readable: spread 16 pictures over the run
            frames = [frames[round(i * (len(frames) - 1) / 15)] for i in range(16)]
        sheet = evidence.contact_sheet(frames, shots_dir / f"{name}_sheet.png") if frames else None
        gif = evidence.animated_gif(frames, shots_dir / f"{name}.gif", ms_per_frame=700) if len(frames) > 1 else None
        media[name] = {"frames": [f"../explore/{name}/screenshots/{f.name}" for f in frames],
                       "sheet": Path(sheet).name if sheet else None, "gif": Path(gif).name if gif else None,
                       "bursts": []}
        # Each action's short recording (move, jump, walk, fight) as its own sheet.
        for steps_file in [ev / "explore" / name / "steps.jsonl"]:
            if not steps_file.exists():
                continue
            for line in steps_file.read_text(encoding="utf-8").splitlines():
                step = json.loads(line)
                burst = [ev / "explore" / name / f for f in step.get("frames") or []]
                if len(burst) >= 2:
                    out = shots_dir / f"{name}_step{step['step']}_{step['action']}_burst.png"
                    if evidence.contact_sheet(burst, out, columns=len(burst) if len(burst) <= 6 else 4, width=400):
                        media[name]["bursts"].append({"step": step["step"], "action": step["action"], "sheet": out.name})
    return media


BURST_PRIORITY = {"attack": 0, "jump": 1, "walk_to": 2, "move": 3, "cast": 4}


def _review_images(names, media, exploration, max_images):
    picked = []
    for name in names:
        if media.get(name, {}).get("sheet"):
            picked.append((f"screenshots/{media[name]['sheet']}", f"{name}: every action's screenshot in order "
                                                                    "(numbered), left to right, top to bottom"))
    # Short recordings: one per scenario first (the most telling action), then more while room is left.
    queues = {n: sorted(media.get(n, {}).get("bursts", []), key=lambda b: (BURST_PRIORITY.get(b["action"], 9), b["step"]))
              for n in names}
    budget = max(0, max_images - len(picked) - len(names))
    while budget > 0 and any(queues.values()):
        for n in names:
            if queues[n] and budget > 0:
                b = queues[n].pop(0)
                picked.append((f"screenshots/{b['sheet']}", f"{n} step {b['step']} ({b['action']}): pictures taken "
                                                             "during the action, in order (a short recording)"))
                budget -= 1
    for name in names:
        steps = [s for s in exploration.get(name, {}).get("steps", []) if s.get("screenshot")]
        if steps and len(picked) < max_images:
            last = steps[-1]
            picked.append((f"explore/{name}/{last['screenshot']}", f"{name}: full-size screenshot after step "
                                                                     f"{last['step']} ({last['action']})"))
    return picked[:max_images]


def uses_gm_commands(scenario):
    return any(step.get("action") == "gm_command" for step in scenario.get("script", []))


def _scenario_text(loaded, exploration, results):
    lines = []
    for s, r in zip(loaded, results):
        e = exploration.get(s["name"], {})
        cheats = " CHEATS ENABLED: the script uses GM commands (setup, not evidence)." if uses_gm_commands(s) else ""
        lines.append(f"- `{s['name']}` ({e.get('mode')}, {e.get('class')} {e.get('character')}): {s['description']}{cheats} "
                     f"{r.get('actions_executed', 0)} actions executed, {r.get('actions_failed', 0)} failed, "
                     f"{r.get('actions_rejected', 0)} rejected" + (f"; stopped: {r['error']}" if r.get("error") else ""))
        for step in e.get("steps", []):
            if step.get("source") == "setup":
                continue
            lines.append(f"  - {step['step']}. {step['action']} {json.dumps(step.get('params', {}))}: "
                         f"{'REJECTED ' + step['rejected'] if step.get('rejected') else ('ok' if step.get('ok') else 'FAILED')}"
                         f" {step.get('result', '')} ({step.get('screenshot') or 'no picture'})")
    return "\n".join(lines)


def _facts(results, exploration, server, tests, findings):
    facts = []
    for r in results:
        steps = [s for s in exploration.get(r["shot"], {}).get("steps", []) if s.get("fps_avg")]
        fps = [s["fps_avg"] for s in steps]
        p5 = [s["fps_p5"] for s in steps if s.get("fps_p5")]
        low = [s["fps_min"] for s in steps if s.get("fps_min")]
        m, cl = r.get("metrics") or {}, r.get("client_log") or {}
        if fps and low:
            facts.append(f"- `{r['shot']}`: frame rate during actions: average {min(fps):.0f}-{max(fps):.0f} fps"
                         + (f", 5th percentile at least {min(p5):.0f} fps" if p5 else "")
                         + f", worst single frame {min(low):.0f} fps (frames around the controller's own pictures "
                           "are left out); ")
        else:
            facts.append(f"- `{r['shot']}`: no frame data; ")
        facts[-1] += (f"client peak memory {m.get('peak_working_set_mb')} MB, CPU {m.get('avg_cpu_percent_of_machine')}% "
                      f"of the machine; {cl.get('problem_lines')} problem lines / {cl.get('exceptions')} exceptions in the "
                      f"client log; {sum(len(s.get('log_errors') or []) for s in exploration.get(r['shot'], {}).get('steps', []))} "
                      f"error log entries seen by the controller.")
    if server:
        facts.append(f"- Server ticks (20 Hz, 50 ms budget): worst average {server.get('tick_avg_ms_max')} ms, worst p99 "
                     f"{server.get('tick_p99_ms_max')} ms over {server.get('stats_lines')} intervals; log levels "
                     f"{server.get('level_counts')}.")
    if tests:
        facts.append(f"- Tests: {tests.get('passed')} passed, {tests.get('failed')} failed of {tests.get('total')}.")
    facts.append(f"- The explorer noted {len(findings)} possible problem(s): see findings.json.")
    return "\n".join(facts)
