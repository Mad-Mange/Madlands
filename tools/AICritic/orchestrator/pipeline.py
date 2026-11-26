"""The project's own build and test entry points, run inside a worktree (CLAUDE.md, "Commands")."""

import json
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

from . import proc
from .config import resolve

TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def now_iso():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def client_exe(worktree):
    return Path(worktree) / "builds" / "client" / "Madlands.exe"


# ---------------------------------------------------------------------------- worktree preparation

def unity_locked(project_dir):
    return (Path(project_dir) / "Temp" / "UnityLockfile").exists()


def library_candidates(cfg, worktree):
    """Unity Library folders that may seed a new worktree: other AI worktrees first (newest first),
    then the main checkout. Folders of an open Unity project are skipped (their database is live)."""
    root = resolve(cfg, "worktree_root")
    found = []
    if root.exists():
        for d in root.iterdir():
            lib = d / "client" / "Library"
            if d.resolve() != Path(worktree).resolve() and lib.is_dir() and not unity_locked(d / "client"):
                found.append(lib)
    found.sort(key=lambda p: p.stat().st_mtime, reverse=True)
    from .config import REPO_ROOT
    main_lib = REPO_ROOT / "client" / "Library"
    if main_lib.is_dir() and not unity_locked(REPO_ROOT / "client"):
        found.append(main_lib)
    return found


def seed_library(cfg, worktree, log_dir):
    """Copies a Unity Library into the worktree. Returns a result dict (never fails the pipeline:
    without a seed Unity simply imports everything, which only takes longer)."""
    target = Path(worktree) / "client" / "Library"
    if target.exists():
        return {"seeded": False, "reason": "worktree already has a Library"}
    if not cfg["git"]["seed_library"]:
        return {"seeded": False, "reason": "disabled in config"}
    candidates = library_candidates(cfg, worktree)
    if not candidates:
        return {"seeded": False, "reason": "no unlocked Library found (Unity will import from scratch)"}
    src = candidates[0]
    log = Path(log_dir) / "library-seed.log"
    if proc.IS_WINDOWS:
        cmd = ["robocopy", str(src), str(target), "/E", "/MT:16", "/R:1", "/W:1", "/NFL", "/NDL", "/NP"]
        r = proc.run("library_seed", cmd, worktree, log, cfg["timeouts"]["library_seed"])
        ok = r.returncode is not None and r.returncode < 8 and not r.timed_out  # robocopy: < 8 is success
    else:
        start = time.monotonic()
        shutil.copytree(src, target)
        ok, r = True, None
    if not ok:
        shutil.rmtree(target, ignore_errors=True)
        return {"seeded": False, "reason": f"copy from {src} failed (see {log})"}
    return {"seeded": True, "source": str(src), "seconds": round(r.seconds, 1) if r else None}


# ---------------------------------------------------------------------------- steps

def dotnet_build(cfg, wt, logs):
    return proc.run("dotnet_build", ["dotnet", "build", "Madlands.slnx", "-c", "Debug", "-nologo"], wt,
                    logs / "dotnet-build.log", cfg["timeouts"]["dotnet_build"], env=proc.clean_env())


def dotnet_test(cfg, wt, logs, results_dir):
    results_dir.mkdir(parents=True, exist_ok=True)
    r = proc.run("dotnet_test",
                 ["dotnet", "test", "Madlands.slnx", "-c", "Debug", "--no-build", "-nologo",
                  "--logger", "trx", "--results-directory", str(results_dir)],
                 wt, logs / "dotnet-test.log", cfg["timeouts"]["dotnet_test"], env=proc.clean_env())
    summary = parse_trx_dir(results_dir)
    summary["log"] = r.log
    (results_dir / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    if r.ok and summary["total"] == 0:
        r.error = "dotnet test reported success but no test results were found"
    if r.ok and summary["failed"] > 0:
        r.error = f"{summary['failed']} test(s) failed"
    return r, summary


def content_validate(cfg, wt, logs):
    return proc.run("content_validate",
                    ["dotnet", "run", "--project", "tools/Madlands.ContentTool", "-c", "Debug", "--no-build",
                     "--", "validate"],
                    wt, logs / "content-validate.log", cfg["timeouts"]["content_validate"], env=proc.clean_env())


def unity_build(cfg, wt, logs, not_before):
    """BuildTools.BuildWindowsFromCommandLine -> builds/client/Madlands.exe. The old client folder is
    deleted first, so a failed build can never leave an older executable behind."""
    unity = resolve(cfg, "unity")
    client_dir = Path(wt) / "builds" / "client"
    if client_dir.exists():
        if Path(wt).resolve() not in client_dir.resolve().parents:
            raise RuntimeError(f"refusing to delete {client_dir}: not inside the worktree")
        shutil.rmtree(client_dir)
    log = logs / "unity-build.log"
    r = proc.run("unity_build",
                 [unity, "-batchmode", "-projectPath", Path(wt) / "client", "-logFile", log,
                  "-executeMethod", "Madlands.Client.Editor.BuildTools.BuildWindowsFromCommandLine"],
                 wt, logs / "unity-build.console.log", cfg["timeouts"]["unity_build"], env=proc.clean_env())
    # Unity writes its own log through -logFile; point the result at it.
    r.log = str(log)
    summary = parse_unity_log(log)
    exe = client_exe(wt)
    if r.ok:
        if not exe.exists():
            r.error = "Unity reported success but builds/client/Madlands.exe is missing"
        elif exe.stat().st_mtime < not_before:
            r.error = "builds/client/Madlands.exe is older than this round (stale build)"
    elif not r.error:
        first = summary["compile_errors"][:1] or summary["build_errors"][:1]
        r.error = f"Unity exited with {r.returncode}" + (f": {first[0]}" if first else "")
    return r, summary


def parse_unity_log(path):
    compile_errors, build_errors, summary_line = [], [], None
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return {"compile_errors": [], "build_errors": ["Unity log missing"], "summary": None}
    for line in text.splitlines():
        if re.search(r"error CS\d{4}", line):
            if line not in compile_errors:
                compile_errors.append(line.strip())
        elif "[BuildTools] Build" in line:
            summary_line = line.strip()
        elif re.search(r"(Build Failed|BuildFailedException|Error building Player|executeMethod.*could not be found)", line):
            build_errors.append(line.strip())
    return {"compile_errors": compile_errors[:30], "build_errors": build_errors[:30], "summary": summary_line}


def parse_trx_dir(results_dir):
    total = passed = failed = skipped = 0
    failures, files = [], []
    for trx in sorted(Path(results_dir).glob("*.trx")):
        files.append(trx.name)
        try:
            root = ET.parse(trx).getroot()
        except ET.ParseError as e:
            failures.append({"test": trx.name, "message": f"unreadable TRX: {e}"})
            continue
        counters = root.find("t:ResultSummary/t:Counters", TRX_NS)
        if counters is not None:
            total += int(counters.get("total", 0))
            passed += int(counters.get("passed", 0))
            failed += int(counters.get("failed", 0)) + int(counters.get("error", 0)) + int(counters.get("timeout", 0))
            skipped += int(counters.get("notExecuted", 0))
        for res in root.iterfind(".//t:UnitTestResult", TRX_NS):
            if res.get("outcome") in ("Failed", "Error", "Timeout"):
                msg = res.find("t:Output/t:ErrorInfo/t:Message", TRX_NS)
                failures.append({"test": res.get("testName"),
                                 "message": (msg.text or "").strip()[:800] if msg is not None else ""})
            elif res.get("outcome") == "NotExecuted" and not counters:
                skipped += 1
    return {"total": total, "passed": passed, "failed": failed, "skipped": skipped,
            "failures": failures[:40], "trx_files": files}


def run_pipeline(cfg, wt, round_dir, not_before):
    """Runs every required step in order and stops at the first failure.
    Returns (ok, steps, failure) where steps is a list of dicts for the report."""
    logs = Path(round_dir) / "evidence" / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    steps = []

    def record(r, extra=None):
        d = r.to_dict()
        d["started"] = now_iso()
        if extra:
            d.update(extra)
        steps.append(d)
        return r.ok

    if not record(dotnet_build(cfg, wt, logs)):
        return False, steps, "dotnet_build"
    r, tests = dotnet_test(cfg, wt, logs, Path(round_dir) / "evidence" / "tests")
    if not record(r, {"tests": {k: v for k, v in tests.items() if k != "failures"},
                      "failures": tests["failures"][:10]}):
        return False, steps, "dotnet_test"
    if not record(content_validate(cfg, wt, logs)):
        return False, steps, "content_validate"
    r, unity = unity_build(cfg, wt, logs, not_before)
    if not record(r, {"unity": unity}):
        return False, steps, "unity_build"
    return True, steps, None


def tail(path, lines=40):
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return ""
    return "\n".join(text[-lines:])
