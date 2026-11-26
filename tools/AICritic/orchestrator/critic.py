"""The Critic: Codex CLI, read-only sandbox, no user config (so no MCP servers or plugins), working in an
evidence folder outside every Git repository. Its only output is the review JSON."""

import json
from pathlib import Path

from . import proc
from .config import TOOL_DIR

SCHEMA = TOOL_DIR / "schemas" / "review.schema.json"


def resolve_command(cfg):
    """Prefer `node codex.js` over the npm .cmd/.ps1 shims: arguments then bypass cmd.exe quoting."""
    exe = proc.which(cfg["critic"]["command"])
    if not exe:
        raise FileNotFoundError(f"Codex CLI '{cfg['critic']['command']}' not found on PATH")
    p = Path(exe)
    if p.suffix.lower() in (".cmd", ".ps1", ".bat", ""):
        js = p.parent / "node_modules" / "@openai" / "codex" / "bin" / "codex.js"
        node = proc.which("node")
        if js.exists() and node:
            return [node, str(js)]
    return [exe]


def command(cfg, stage_dir, schema, output_file, images, effort=None):
    c = cfg["critic"]
    cmd = resolve_command(cfg) + [
        "exec",
        "--ignore-user-config", "--ephemeral", "--skip-git-repo-check",
        "--sandbox", "read-only",
        "-c", f'windows.sandbox="{c["windows_sandbox"]}"',
        "-c", f'model_reasoning_effort="{effort or c["reasoning_effort"]}"',
        "-c", "project_doc_max_bytes=0",
        "--cd", str(stage_dir),
        "--output-schema", str(schema),
        "--output-last-message", str(output_file),
        "--color", "never",
    ]
    if c["model"]:
        cmd += ["--model", c["model"]]
    for img in images:
        cmd += ["--image", str(img)]
    # A boolean flag last, so the image list ends; the prompt itself comes on stdin.
    cmd += ["--json"]
    return cmd


def token_usage(events_file):
    """Sums the usage of every completed turn in Codex's JSONL event stream."""
    usage = {"input_tokens": 0, "cached_input_tokens": 0, "output_tokens": 0, "reasoning_output_tokens": 0}
    errors = []
    try:
        lines = Path(events_file).read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return usage, ["no event stream"]
    for line in lines:
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            ev = json.loads(line)
        except json.JSONDecodeError:
            continue
        kind = ev.get("type", "")
        if kind == "turn.completed" and isinstance(ev.get("usage"), dict):
            for k in usage:
                usage[k] += int(ev["usage"].get(k) or 0)
        elif kind in ("error", "turn.failed"):
            msg = ev.get("message") or (ev.get("error") or {}).get("message") or json.dumps(ev)[:300]
            errors.append(msg)
    usage["total_tokens"] = usage["input_tokens"] + usage["output_tokens"]
    return usage, errors


def run(cfg, stage_dir, out_dir, prompt, images, timeout, schema=SCHEMA, effort=None):
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    prompt_file = out_dir / "critic-prompt.md"
    prompt_file.write_text(prompt, encoding="utf-8")
    raw = out_dir / "critic-review.raw.json"
    events = out_dir / "critic-events.jsonl"
    if raw.exists():
        raw.unlink()
    r = proc.run("critic", command(cfg, stage_dir, schema, raw, images, effort), stage_dir,
                 out_dir / "critic.log", timeout, env=proc.clean_env(), stdin_path=prompt_file, stdout_path=events)
    usage, errors = token_usage(events)
    result = {"ok": False, "returncode": r.returncode, "timed_out": r.timed_out, "seconds": round(r.seconds, 1),
              "usage": usage, "errors": errors, "log": r.log, "raw": str(raw), "review": None, "error": r.error}
    if not r.ok:
        result["error"] = r.error or f"codex exited with {r.returncode}" + (f": {errors[-1]}" if errors else "")
        return result
    try:
        result["review"] = json.loads(raw.read_text(encoding="utf-8"))
        result["ok"] = True
    except (OSError, json.JSONDecodeError) as e:
        result["error"] = f"the critic's final message is not valid JSON: {e}"
    return result
