"""The Builder: Claude Code CLI in print mode, working only inside the run's worktree."""

import json
from pathlib import Path

from . import proc


def resolve_command(cfg):
    exe = proc.which(cfg["builder"]["command"])
    if not exe:
        raise FileNotFoundError(f"Claude CLI '{cfg['builder']['command']}' not found on PATH")
    return exe


def command(cfg, budget_usd, session_id=None, tools_override=None):
    b = cfg["builder"]
    cmd = [resolve_command(cfg), "-p", "--output-format", "json",
           "--permission-mode", "acceptEdits",
           "--max-budget-usd", f"{budget_usd:.2f}",
           # No MCP servers: the builder works from the repository alone.
           "--strict-mcp-config"]
    if tools_override is not None:
        cmd += ["--tools", tools_override]
    else:
        cmd += ["--allowedTools", ",".join(b["allowed_tools"]),
                "--disallowedTools", ",".join(b["disallowed_tools"])]
    if b["model"]:
        cmd += ["--model", b["model"]]
    if b["effort"]:
        cmd += ["--effort", b["effort"]]
    if session_id:
        cmd += ["--resume", session_id]
    return cmd


def parse_output(path):
    """Claude's --output-format json result object, or an error dict."""
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace").strip()
    except OSError as e:
        return {"is_error": True, "error": f"no output: {e}"}
    if not text:
        return {"is_error": True, "error": "empty output"}
    # The result is the last JSON object on stdout.
    for line in reversed(text.splitlines()):
        line = line.strip()
        if line.startswith("{"):
            try:
                return json.loads(line)
            except json.JSONDecodeError:
                break
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {"is_error": True, "error": "output is not JSON", "raw": text[:2000]}


def run(cfg, worktree, round_dir, prompt, budget_usd, timeout, session_id=None):
    round_dir = Path(round_dir)
    prompt_file = round_dir / "builder-prompt.md"
    prompt_file.write_text(prompt, encoding="utf-8")
    out_file = round_dir / "builder-output.json"
    r = proc.run("builder", command(cfg, budget_usd, session_id), worktree, round_dir / "builder.log",
                 timeout, env=proc.clean_env(), stdin_path=prompt_file, stdout_path=out_file)
    data = parse_output(out_file)
    cost = float(data.get("total_cost_usd") or 0.0)
    ok = r.ok and not data.get("is_error") and data.get("subtype", "success") == "success"
    return {
        "ok": ok,
        "returncode": r.returncode,
        "timed_out": r.timed_out,
        "seconds": round(r.seconds, 1),
        "session_id": data.get("session_id") or session_id,
        "cost_usd": cost,
        "subtype": data.get("subtype"),
        "num_turns": data.get("num_turns"),
        "summary": (data.get("result") or "")[:6000],
        "error": r.error or data.get("error") or ("" if ok else f"builder ended with {data.get('subtype') or 'an error'}"),
        "log": r.log,
        "output": str(out_file),
    }
