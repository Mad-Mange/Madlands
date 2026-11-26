"""Stand-ins for dry runs: a fake builder and critic (no AI calls) and, with --fake-pipeline, a fake
worktree, build and play session (no Unity, no .NET, a few seconds per round)."""

import json
import struct
import subprocess
import time
import zlib
from pathlib import Path

from . import CRITERIA
from .pipeline import now_iso

SCENARIOS = ("improve-then-pass", "pass", "always-improve", "blocked", "build-fail", "critic-writes",
             "builder-fail")

_SCORES = {
    "improve-then-pass": [6.0, 7.0, 8.5],
    "pass": [8.6],
    "always-improve": [6.0, 6.5, 7.0],
    "blocked": [7.0],
    "build-fail": [7.0],
    "critic-writes": [7.0],
    "builder-fail": [7.0],
}


def tiny_png(path, w=64, h=36, rgb=(80, 120, 60)):
    raw = b"".join(b"\x00" + bytes(rgb) * w for _ in range(h))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(png)


def init_repo(path, branch):
    """A throwaway Git repository standing in for the worktree (--fake-pipeline)."""
    path = Path(path)
    path.mkdir(parents=True, exist_ok=True)
    ident = ["-c", "user.name=aicritic", "-c", "user.email=aicritic@localhost"]
    subprocess.run(["git", "init", "-q", "-b", branch], cwd=path, check=True)
    (path / "README.md").write_text("Fake worktree for an AICritic dry run.\n", encoding="utf-8")
    (path / ".gitignore").write_text("builds/\n", encoding="utf-8")
    subprocess.run(["git", "add", "-A"], cwd=path, check=True)
    subprocess.run(["git", *ident, "commit", "-q", "-m", "base"], cwd=path, check=True)


def builder(worktree, round_no, scenario, round_dir, prompt):
    Path(round_dir, "builder-prompt.md").write_text(prompt, encoding="utf-8")
    if scenario == "builder-fail":
        return {"ok": False, "returncode": 1, "timed_out": False, "seconds": 0.0, "session_id": None,
                "cost_usd": 0.0, "subtype": "error_during_execution", "num_turns": 0, "summary": "",
                "error": "fake builder failure", "log": None, "output": None}
    notes = Path(worktree) / "notes.txt"
    with open(notes, "a", encoding="utf-8") as f:
        f.write(f"round {round_no}: fake builder change\n")
    return {"ok": True, "returncode": 0, "timed_out": False, "seconds": 0.0, "session_id": "fake-session",
            "cost_usd": 0.0, "subtype": "success", "num_turns": 1,
            "summary": f"Dry run: appended a line to notes.txt (round {round_no}).", "error": "",
            "log": None, "output": None}


def pipeline(round_dir, round_no, scenario, worktree):
    logs = Path(round_dir) / "evidence" / "logs"
    tests = Path(round_dir) / "evidence" / "tests"
    logs.mkdir(parents=True, exist_ok=True)
    tests.mkdir(parents=True, exist_ok=True)
    steps = []
    for name in ("dotnet_build", "dotnet_test", "content_validate", "unity_build"):
        log = logs / f"{name}.log"
        fail = scenario == "build-fail" and round_no == 1 and name == "unity_build"
        log.write_text(f"[fake] {name} {'failed' if fail else 'ok'}\n", encoding="utf-8")
        steps.append({"name": name, "ok": not fail, "returncode": 1 if fail else 0, "seconds": 0.0,
                      "log": str(log), "error": "fake Unity compile error CS0103" if fail else "",
                      "started": now_iso()})
        if fail:
            return False, steps, name
    summary = {"total": 3, "passed": 3, "failed": 0, "skipped": 0, "failures": [], "trx_files": []}
    (tests / "summary.json").write_text(json.dumps(summary), encoding="utf-8")
    steps[1]["tests"] = summary
    exe = Path(worktree) / "builds" / "client" / "Madlands.exe"
    exe.parent.mkdir(parents=True, exist_ok=True)
    exe.write_bytes(b"fake client")
    return True, steps, None


def capture(scene, round_dir):
    shots_dir = Path(round_dir) / "evidence" / "screenshots"
    logs = Path(round_dir) / "evidence" / "logs"
    time.sleep(0.05)  # keep the pictures strictly newer than the fake build
    result = {"started": now_iso(), "shots": [], "bots": None, "server": {"stats_lines": 0}}
    for shot in scene["shot"]:
        burst = int(shot.get("burst", 1))
        names = [f"{shot['name']}_{i}.png" for i in range(1, burst + 1)] if burst > 1 else [f"{shot['name']}.png"]
        for n in names:
            tiny_png(shots_dir / n)
        (logs / f"client-{shot['name']}.log").write_text("[fake] client log\n", encoding="utf-8")
        result["shots"].append({"shot": shot["name"], "ok": True, "files": names, "missing": [],
                                "metrics": {}, "log": str(logs / f"client-{shot['name']}.log"),
                                "seconds": 0.0, "name": f"shot_{shot['name']}"})
    result["finished"] = now_iso()
    return True, result, None


def review(round_no, scenario):
    scores = _SCORES.get(scenario, _SCORES["improve-then-pass"])
    s = scores[min(round_no, len(scores)) - 1]
    criteria = {}
    for name in CRITERIA:
        blank = scenario == "blocked" and name == "performance"
        criteria[name] = {
            "evaluable": not blank, "score": None if blank else s, "confidence": "low",
            "rationale": "Dry run: fixed score.", "evidence": [] if blank else ["screenshots/village_hud.png"],
            "missing_evidence": "Dry run: no frame-time data." if blank else "",
        }
    return {
        "summary": f"Dry-run review for round {round_no}.",
        "task_assessment": {"completed": "unknown", "explanation": "Dry run."},
        "criteria": criteria,
        "issues": [
            {"rank": 1, "severity": "major", "criterion": "visual_quality", "in_scope": True,
             "title": "Dry-run issue", "detail": "A placeholder issue for the feedback prompt.",
             "evidence": ["screenshots/village_hud.png"], "suggested_fix": "Nothing: this is a dry run."},
            {"rank": 2, "severity": "minor", "criterion": "performance", "in_scope": False,
             "title": "Out-of-scope placeholder", "detail": "Shows how out-of-scope issues are listed.",
             "evidence": [], "suggested_fix": "None."},
        ],
    }


def critic(round_no, scenario, out_dir, prompt, worktree):
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "critic-prompt.md").write_text(prompt, encoding="utf-8")
    if scenario == "critic-writes":
        # Simulates a critic that broke the rules: the orchestrator must notice and block.
        (Path(worktree) / "critic-was-here.txt").write_text("x", encoding="utf-8")
    rev = review(round_no, scenario)
    (out_dir / "critic-review.raw.json").write_text(json.dumps(rev, indent=2), encoding="utf-8")
    return {"ok": True, "returncode": 0, "timed_out": False, "seconds": 0.0,
            "usage": {"total_tokens": 0, "input_tokens": 0, "output_tokens": 0}, "errors": [],
            "log": None, "raw": str(out_dir / "critic-review.raw.json"), "review": rev, "error": ""}
