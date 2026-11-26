"""Configuration: built-in defaults, overridden by an optional TOML file."""

import copy
import os
import tomllib
from pathlib import Path

from . import CRITERIA, HARD_MAX_ROUNDS

TOOL_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT = TOOL_DIR.parent.parent

DEFAULTS = {
    "paths": {
        "unity": "C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe",
        # Worktrees and the critic's evidence copies live here, outside the main checkout.
        "worktree_root": "../Madlands-aicritic",
        # Reports, prompts, logs and screenshots of every run (gitignored).
        "runs_dir": "builds/aicritic",
    },
    "git": {
        "base_ref": "main",
        "branch_prefix": "aicritic/",
        # Copy an existing Unity Library into a new worktree so Unity does not import everything again.
        "seed_library": True,
    },
    "loop": {
        "max_rounds": 3,
        "pass_score": 8.0,
        "max_run_minutes": 240,
    },
    "budget": {
        # Claude: hard cap per builder call (--max-budget-usd) and for the whole run.
        "builder_usd_per_round": 5.0,
        "builder_usd_total": 12.0,
        # Codex reports tokens, not dollars: the loop stops before a critic call once this is used up.
        "critic_tokens_total": 3_000_000,
    },
    "builder": {
        "command": "claude",
        "model": "",
        "effort": "",
        "allowed_tools": [
            "Read", "Edit", "Write", "Glob", "Grep",
            "Bash(dotnet build:*)", "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)",
        ],
        "disallowed_tools": [
            "Agent", "WebFetch", "WebSearch",
            "Bash(git commit:*)", "Bash(git push:*)", "Bash(git checkout:*)", "Bash(git switch:*)",
            "Bash(git reset:*)", "Bash(git rebase:*)", "Bash(git stash:*)", "Bash(git merge:*)",
        ],
        # The builder must not touch these (checked after every builder call).
        "protected_paths": ["tools/AICritic/", ".github/"],
    },
    "critic": {
        "command": "codex",
        "model": "gpt-6.1-terra",
        "reasoning_effort": "high",
        # The user config is ignored (no MCP servers, no plugins), so the Windows sandbox mode is passed here.
        "windows_sandbox": "elevated",
        "max_images": 10,
    },
    "timeouts": {  # seconds
        "builder": 1800,
        "dotnet_build": 900,
        "dotnet_test": 1500,
        "content_validate": 300,
        "unity_build": 3600,
        "library_seed": 900,
        "worktree": 900,
        "server_start": 180,
        "client_margin": 60,
        "bots": 180,
        "critic": 1800,
        "check_ai": 300,
    },
    "weights": {
        "visual_quality": 25,
        "gameplay": 30,
        "animation_physics": 20,
        "bugs_stability": 15,
        "performance": 10,
    },
    "realm": {
        "port": 7787,
        "http_port": 7790,
        "seed": 1,
        "stats_interval": 5,
        "bots": 20,
        "bot_seconds": 20,
    },
    "scene": {
        "file": "scenes/hollowmere_smoke.toml",
    },
}


class ConfigError(Exception):
    pass


def _merge(base, override, path=""):
    for key, value in override.items():
        where = f"{path}.{key}" if path else key
        if key not in base:
            raise ConfigError(f"unknown config key '{where}'")
        if isinstance(base[key], dict):
            if not isinstance(value, dict):
                raise ConfigError(f"'{where}' must be a table")
            _merge(base[key], value, where)
        else:
            base[key] = value


def load(path=None):
    cfg = copy.deepcopy(DEFAULTS)
    if path is None:
        candidate = TOOL_DIR / "config.toml"
        path = candidate if candidate.exists() else None
    if path is not None:
        with open(path, "rb") as f:
            _merge(cfg, tomllib.load(f))
    cfg["_source"] = str(path) if path else "built-in defaults"
    validate(cfg)
    return cfg


def validate(cfg):
    weights = cfg["weights"]
    if set(weights) != set(CRITERIA):
        raise ConfigError(f"weights must name exactly {', '.join(CRITERIA)}")
    if any((not isinstance(w, (int, float))) or w <= 0 for w in weights.values()):
        raise ConfigError("weights must be positive numbers")
    if abs(sum(weights.values()) - 100) > 1e-6:
        raise ConfigError(f"weights must add up to 100 (they add up to {sum(weights.values())})")
    rounds = cfg["loop"]["max_rounds"]
    if not isinstance(rounds, int) or not 1 <= rounds <= HARD_MAX_ROUNDS:
        raise ConfigError(f"loop.max_rounds must be 1..{HARD_MAX_ROUNDS}")
    if not 0 < cfg["loop"]["pass_score"] <= 10:
        raise ConfigError("loop.pass_score must be in (0, 10]")
    for key in ("builder_usd_per_round", "builder_usd_total"):
        if cfg["budget"][key] <= 0:
            raise ConfigError(f"budget.{key} must be positive")
    for key, value in cfg["timeouts"].items():
        if not isinstance(value, (int, float)) or value <= 0:
            raise ConfigError(f"timeouts.{key} must be a positive number of seconds")


def resolve(cfg, key):
    """An absolute path for a [paths] entry (relative entries are relative to the repo root)."""
    p = Path(os.path.expandvars(cfg["paths"][key]))
    return (p if p.is_absolute() else REPO_ROOT / p).resolve()


def load_scene(cfg):
    p = Path(cfg["scene"]["file"])
    if not p.is_absolute():
        p = TOOL_DIR / p
    with open(p, "rb") as f:
        scene = tomllib.load(f)
    shots = scene.get("shot", [])
    if not shots:
        raise ConfigError(f"scene {p} has no [[shot]] entries")
    names = set()
    for shot in shots:
        name = shot.get("name", "")
        if not name.replace("_", "").isalnum() or name in names:
            raise ConfigError(f"scene shot names must be unique and alphanumeric: '{name}'")
        names.add(name)
    scene["_path"] = str(p)
    return scene
