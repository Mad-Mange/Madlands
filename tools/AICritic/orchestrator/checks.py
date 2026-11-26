"""`aicritic.py check`: validates each part on its own before any loop runs.

  tools     the CLIs, Unity, Pillow, free realm ports
  claude    one tiny Claude print-mode call (no tools)
  codex     one tiny Codex call: structured output, an attached image, and a write attempt that the
            read-only sandbox must block
  pipeline  a fresh detached worktree of the base ref: dotnet build, dotnet test, content validation,
            the Unity Windows client build
  capture   the scripted play session against that build, plus the freshness check
"""

import json
import shutil
import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path

from . import builder, capture, critic, evidence, fakes, gitops, pipeline, proc
from .config import REPO_ROOT, TOOL_DIR, load_scene, resolve

ALL = ("tools", "claude", "codex", "pipeline", "capture")


def _version(cmd):
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
        return (p.stdout or p.stderr).strip().splitlines()[0] if p.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired, IndexError):
        return None


def check_tools(cfg, out):
    items = {}
    claude_exe = proc.which(cfg["builder"]["command"])
    items["claude"] = _version([claude_exe, "--version"]) if claude_exe else None
    try:
        items["codex"] = _version(critic.resolve_command(cfg) + ["--version"])
    except FileNotFoundError:
        items["codex"] = None
    items["dotnet"] = _version(["dotnet", "--version"])
    items["git"] = _version(["git", "--version"])
    items["git_lfs"] = _version(["git", "lfs", "version"])
    unity = resolve(cfg, "unity")
    items["unity"] = str(unity) if unity.exists() else None
    items["pillow"] = evidence.Image is not None
    items["python"] = proc.python_exe()
    realm = cfg["realm"]
    items["realm_ports_free"] = capture.port_free(realm["port"], udp=True) and capture.port_free(realm["http_port"])
    root = resolve(cfg, "worktree_root")
    root.mkdir(parents=True, exist_ok=True)
    items["free_disk_gb"] = round(shutil.disk_usage(root).free / 2**30, 1)
    inside_repo = subprocess.run(["git", "rev-parse", "--show-toplevel"], cwd=root, capture_output=True).returncode == 0
    items["worktree_root_outside_git"] = not inside_repo
    required = ("claude", "codex", "dotnet", "git", "git_lfs", "unity")
    problems = [k for k in required if not items[k]]
    if not items["realm_ports_free"]:
        problems.append("realm ports in use")
    if items["free_disk_gb"] < 15:
        problems.append("less than 15 GB free for worktrees")
    if not items["worktree_root_outside_git"]:
        problems.append("paths.worktree_root lies inside a Git repository (the critic must work outside one)")
    return {"ok": not problems, "items": items, "problems": problems}


def check_claude(cfg, out):
    d = out / "claude"
    d.mkdir(parents=True, exist_ok=True)
    prompt = d / "prompt.md"
    prompt.write_text("This is a connectivity self-test. Reply with exactly AICRITIC_OK and nothing else.\n",
                      encoding="utf-8")
    cmd = builder.command(cfg, 0.25, tools_override="") + ["--no-session-persistence"]
    r = proc.run("claude_check", cmd, d, d / "claude.log", cfg["timeouts"]["check_ai"], env=proc.clean_env(),
                 stdin_path=prompt, stdout_path=d / "output.json")
    data = builder.parse_output(d / "output.json")
    answer = (data.get("result") or "").strip()
    ok = r.ok and not data.get("is_error") and "AICRITIC_OK" in answer
    return {"ok": ok, "answer": answer[:200], "cost_usd": data.get("total_cost_usd"),
            "seconds": round(r.seconds, 1), "error": r.error or data.get("error") or ("" if ok else "unexpected answer"),
            "log": r.log}


def check_codex(cfg, out):
    stage = resolve(cfg, "worktree_root") / "_critic" / f"check-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
    stage.mkdir(parents=True)
    image = stage / "probe.png"
    fakes.tiny_png(image, 96, 96, (220, 20, 20))
    schema = TOOL_DIR / "schemas" / "check.schema.json"
    prompt = ("This is a self-test of an automated review harness.\n"
              "1. Name the dominant color of the attached image in one lowercase word.\n"
              "2. Try once to create a file named probe.txt in the current directory with a shell command, for "
              "example `echo test > probe.txt`. If it is blocked, do not try any workaround.\n"
              "3. Answer with the JSON the schema asks for: the color, whether you attempted the write, whether "
              "it was blocked, and a short note.\n")
    before = evidence.folder_fingerprint(stage)
    r = critic.run(cfg, stage, out / "codex", prompt, [image], cfg["timeouts"]["check_ai"], schema=schema,
                   effort="low")
    after = evidence.folder_fingerprint(stage)
    answer = r["review"] or {}
    problems = []
    if not r["ok"]:
        problems.append(r["error"])
    if (stage / "probe.txt").exists() or before != after:
        problems.append("the read-only sandbox let Codex write a file")
    if r["ok"] and "red" not in str(answer.get("image_color", "")).lower():
        problems.append(f"image input not understood (answered {answer.get('image_color')!r})")
    return {"ok": not problems, "answer": answer, "usage": r["usage"], "seconds": r["seconds"],
            "stage": str(stage), "problems": problems, "log": r["log"]}


def check_pipeline(cfg, out, state):
    root = resolve(cfg, "worktree_root")
    wt = root / f"check-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
    t0 = time.time()
    gitops.add_worktree(REPO_ROOT, wt, cfg["git"]["base_ref"])
    seed = pipeline.seed_library(cfg, wt, out)
    state["worktree"] = wt
    round_dir = out / "pipeline"
    round_dir.mkdir(parents=True, exist_ok=True)
    ok, steps, failed = pipeline.run_pipeline(cfg, wt, round_dir, t0)
    return {"ok": ok, "worktree": str(wt), "commit": gitops.rev(wt), "library_seed": seed, "failed_step": failed,
            "steps": steps, "seconds": round(time.time() - t0, 1)}


def check_capture(cfg, out, state):
    wt = state.get("worktree")
    if wt is None:
        return {"ok": False, "problems": ["run the pipeline check first (it builds the client this check plays)"]}
    exe = pipeline.client_exe(wt)
    scene = load_scene(cfg)
    round_dir = out / "pipeline"
    t0 = time.time()
    ok, cap, failure = capture.run_capture(cfg, scene, wt, round_dir, "chk")
    media = evidence.prepare_media(scene, round_dir)
    manifest = evidence.build_manifest(round_dir, {"capture": cap, "media": media})
    stale = evidence.freshness_problems(manifest, state.get("started", t0), exe.stat().st_mtime if exe.exists() else None)
    problems = ([failure] if failure else []) + stale
    return {"ok": ok and not stale, "problems": problems, "shots": cap["shots"], "server": cap["server"],
            "bots": cap["bots"], "media": media, "seconds": round(time.time() - t0, 1)}


def run_checks(cfg, only=None, worktree=None):
    """worktree: an existing check worktree whose client build `capture` should play (skips `pipeline`)."""
    only = only or list(ALL)
    out = resolve(cfg, "runs_dir") / "checks" / datetime.now().strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True)
    results = {"started": datetime.now(timezone.utc).isoformat(timespec="seconds"), "config": cfg["_source"],
               "base_ref": cfg["git"]["base_ref"], "checks": {}}
    state = {"started": time.time()}
    if worktree:
        state["worktree"] = Path(worktree).resolve()
        # The pictures must still be newer than that worktree's client build.
        exe = pipeline.client_exe(state["worktree"])
        state["started"] = exe.stat().st_mtime if exe.exists() else time.time()
    funcs = {"tools": lambda: check_tools(cfg, out), "claude": lambda: check_claude(cfg, out),
             "codex": lambda: check_codex(cfg, out), "pipeline": lambda: check_pipeline(cfg, out, state),
             "capture": lambda: check_capture(cfg, out, state)}
    for name in ALL:
        if name not in only:
            continue
        print(f"[aicritic] check {name} ...", flush=True)
        try:
            res = funcs[name]()
        except Exception as e:  # a check that crashes is a failed check, reported like the others
            res = {"ok": False, "problems": [f"{type(e).__name__}: {e}"]}
        results["checks"][name] = res
        (out / "check.json").write_text(json.dumps(results, indent=2, default=str), encoding="utf-8")
        detail = res.get("error") or "; ".join(res.get("problems") or []) or res.get("failed_step") or ""
        print(f"[aicritic] check {name}: {'OK' if res['ok'] else 'FAILED'} {detail}", flush=True)
        if not res["ok"]:
            print(f"[aicritic] stopping at the first failed check; details in {out / 'check.json'}")
            break
    results["ok"] = all(r["ok"] for r in results["checks"].values()) and set(results["checks"]) == set(only)
    (out / "check.json").write_text(json.dumps(results, indent=2, default=str), encoding="utf-8")
    print(f"[aicritic] checks {'passed' if results['ok'] else 'FAILED'}: {out / 'check.json'}")
    return results
