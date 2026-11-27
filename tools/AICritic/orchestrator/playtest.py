"""The Python side of the Playtest Controller (client/Assets/Madlands/Scripts/Playtest/PlaytestController.cs):
the action allowlist and its validation, scenario files, and the file mailbox session with the client."""

import json
import os
import re
import threading
import time
import tomllib
from pathlib import Path

from . import fakes, proc
from .config import TOOL_DIR

DIRECTIONS = ("forward", "backward", "left", "right")
WINDOWS = ("bags", "character", "questlog", "spellbook", "map")
SCENARIO_DIR = TOOL_DIR / "scenarios"

# The actions a scenario script or the Codex explorer may choose. Each parameter:
# (kind, required, (min, max) or choices, default). Anything else is rejected before it reaches the client.
ACTIONS = {
    "wait": {"seconds": ("num", True, (0.2, 10), None)},
    "move": {"direction": ("enum", True, DIRECTIONS, None), "seconds": ("num", True, (0.2, 6), None),
             "sprint": ("bool", False, None, False)},
    "turn": {"degrees": ("num", True, (-180, 180), None)},
    "face": {"yaw": ("num", True, (0, 360), None)},
    "jump": {},
    "camera": {"pitch": ("num", False, (5, 80), None), "distance": ("num", False, (2, 40), None),
               "yaw_offset": ("num", False, (-180, 180), None)},
    "walk_to": {"x": ("num", True, (-8192, 8192), None), "z": ("num", True, (-8192, 8192), None),
                "seconds": ("num", False, (1, 30), 15)},
    "goto": {"location": ("location", True, None, None)},
    "target_nearest": {},
    "target": {"name": ("name", True, None, None)},
    "attack": {"seconds": ("num", False, (1, 12), 6)},
    "cast": {"slot": ("int", True, (1, 12), None)},
    "interact": {"name": ("name", True, None, None)},
    "interact_object": {},
    "loot": {},
    "open_window": {"window": ("enum", True, WINDOWS, None)},
    "close_windows": {},
    "screenshot": {},
    "reset": {},
}
ACTION_HELP = {
    "wait": "let time pass (seconds 0.2-10)",
    "move": "hold a movement key: direction forward/backward/left/right (strafe), seconds 0.2-6, optional sprint",
    "turn": "turn by degrees (-180..180, positive = right)",
    "face": "face a compass direction (yaw 0-360, 0 = north/+Z, 90 = east/+X)",
    "jump": "jump once",
    "camera": "frame the camera: pitch 5-80, distance 2-40, yaw_offset -180..180 (any subset)",
    "walk_to": "walk straight to world x, z (seconds 1-30, default 15); reports reached, stuck or out of time",
    "goto": "teleport to a named location (list below)",
    "target_nearest": "target the nearest attackable creature",
    "target": "target the nearest unit or object with this exact name (from the observation)",
    "attack": "fight the target (or the nearest enemy) for seconds 1-12: face it, melee, cast spells",
    "cast": "press action bar button slot 1-12",
    "interact": "talk to / use the nearest unit or object with this exact name (fails if the server refuses, e.g. too far away)",
    "interact_object": "use the nearest object within 6 m (gather, open a chest)",
    "loot": "open the loot of the nearest lootable corpse within 6 m",
    "open_window": "open a window: bags, character, questlog, spellbook, map",
    "close_windows": "close open windows (Esc) and clear the target",
    "screenshot": "take a picture without doing anything",
    "reset": "revive, heal and teleport back to the scenario's start",
}
_NAME = re.compile(r"^[A-Za-z][A-Za-z '\-]{0,39}$")
HARD_MAX_ACTIONS = 40


class ActionError(Exception):
    pass


def validate_action(name, params, locations):
    """Returns the cleaned parameters, or raises ActionError. params may hold nulls for unused fields."""
    if name not in ACTIONS:
        raise ActionError(f"unknown action '{name}'")
    params = {k: v for k, v in (params or {}).items() if v is not None}
    spec = ACTIONS[name]
    extra = set(params) - set(spec)
    if extra:
        raise ActionError(f"{name} does not take {', '.join(sorted(extra))}")
    clean = {}
    for key, (kind, required, rng, default) in spec.items():
        if key not in params:
            if required:
                raise ActionError(f"{name} needs {key}")
            if default is not None:
                clean[key] = default
            continue
        v = params[key]
        if kind in ("num", "int"):
            if isinstance(v, bool) or not isinstance(v, (int, float)) or v != v:
                raise ActionError(f"{name}.{key} must be a number")
            if kind == "int" and int(v) != v:
                raise ActionError(f"{name}.{key} must be a whole number")
            if not rng[0] <= v <= rng[1]:
                raise ActionError(f"{name}.{key} must be within {rng[0]}..{rng[1]}")
            clean[key] = int(v) if kind == "int" else float(v)
        elif kind == "enum":
            if v not in rng:
                raise ActionError(f"{name}.{key} must be one of {', '.join(rng)}")
            clean[key] = v
        elif kind == "bool":
            if not isinstance(v, bool):
                raise ActionError(f"{name}.{key} must be true or false")
            clean[key] = v
        elif kind == "name":
            if not isinstance(v, str) or not _NAME.match(v):
                raise ActionError(f"{name}.{key} must be a name of letters, spaces, ' or -")
            clean[key] = v
        elif kind == "location":
            if v not in locations:
                raise ActionError(f"unknown location '{v}' (known: {', '.join(sorted(locations))})")
            clean[key] = v
    if name == "camera" and not clean:
        raise ActionError("camera needs at least one of pitch, distance, yaw_offset")
    return clean


def to_commands(name, params, scenario):
    """The controller commands for one validated action (reset and goto expand here, so the explorer
    never sends GM commands itself)."""
    loc = scenario["locations"]
    if name == "goto":
        x, z = loc[params["location"]]
        return [{"action": "teleport", "x": x, "z": z}]
    if name == "reset":
        x, z = loc[scenario["start"]]
        return [{"action": "close_windows"}, {"action": "gm", "text": ".revive"}, {"action": "gm", "text": ".heal"},
                {"action": "teleport", "x": x, "z": z}, {"action": "gm", "text": ".respawn"}]
    if name == "camera":
        cmd = {"action": "camera"}
        for key in ("pitch", "distance", "yaw_offset"):
            if key in params:
                cmd[key] = params[key]
                cmd["has_" + key] = True
        return [cmd]
    return [{"action": name, **params}]


def action_duration(cmd):
    """How long the client may need for a command, before the margin."""
    a = cmd["action"]
    if a in ("wait", "move", "attack"):
        return float(cmd.get("seconds", 6))
    if a == "walk_to":
        return float(cmd.get("seconds", 15))
    return 3.0


# ---------------------------------------------------------------------------- scenarios

def load_scenario(name):
    path = SCENARIO_DIR / f"{name}.toml"
    if not path.exists():
        raise FileNotFoundError(f"no scenario '{name}' in {SCENARIO_DIR}")
    with open(path, "rb") as f:
        scenario = tomllib.load(f)
    with open(SCENARIO_DIR / "locations.toml", "rb") as f:
        locations = {k: tuple(v) for k, v in tomllib.load(f)["locations"].items()}
    scenario["locations"] = locations
    scenario.setdefault("name", name)
    if scenario.get("start") not in locations:
        raise ValueError(f"scenario {name}: start '{scenario.get('start')}' is not a known location")
    for i, step in enumerate(scenario.get("script", [])):
        step = dict(step)
        action = step.pop("action")
        try:
            validate_action(action, step, locations)
        except ActionError as e:
            raise ValueError(f"scenario {name}, script step {i + 1}: {e}") from None
    return scenario


def scenario_names():
    return sorted(p.stem for p in SCENARIO_DIR.glob("*.toml") if p.stem != "locations")


def allowlist_text(locations):
    lines = [f"- `{name}`: {ACTION_HELP[name]}" for name in ACTIONS]
    lines.append("")
    lines.append("Locations for `goto`: " + ", ".join(f"`{k}` ({v[0]:.0f}, {v[1]:.0f})" for k, v in sorted(locations.items())))
    return "\n".join(lines)


# ---------------------------------------------------------------------------- mailbox session

class ClientGone(Exception):
    pass


def _read_json(path, retries=10):
    for _ in range(retries):
        try:
            return json.loads(Path(path).read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            time.sleep(0.1)
    raise ClientGone(f"could not read {path}")


def _write_atomic(path, data):
    tmp = Path(str(path) + ".tmp")
    tmp.write_text(json.dumps(data), encoding="utf-8")
    os.replace(tmp, path)


class Session:
    """Talks to one running client through its mailbox folder."""

    def __init__(self, mailbox, alive, margin=20.0, heartbeat_timeout=20.0):
        self.mailbox = Path(mailbox)
        self.alive = alive  # callable: is the client process still running?
        self.margin = margin
        self.heartbeat_timeout = heartbeat_timeout
        self.seq = 0
        for sub in ("commands", "observations", "screenshots"):
            (self.mailbox / sub).mkdir(parents=True, exist_ok=True)

    def status(self):
        p = self.mailbox / "status.json"
        if not p.exists():
            return None
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return None

    def wait_ready(self, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if not self.alive():
                raise ClientGone("the client exited before it entered the world")
            s = self.status()
            if s and s.get("in_world") and s.get("state") == "ready":
                return s
            time.sleep(0.25)
        raise ClientGone(f"the client did not enter the world within {timeout:.0f} s")

    def send(self, cmd):
        """Sends one command and waits for the client's observation. Raises ClientGone if the client dies,
        stops answering, or answers without a screenshot."""
        self.seq += 1
        cmd = dict(cmd, seq=self.seq)
        _write_atomic(self.mailbox / "commands" / f"{self.seq:04d}.json", cmd)
        obs_path = self.mailbox / "observations" / f"{self.seq:04d}.json"
        deadline = time.monotonic() + action_duration(cmd) + self.margin
        last_beat = time.monotonic()
        last_frame = None
        while time.monotonic() < deadline:
            if obs_path.exists():
                obs = _read_json(obs_path)
                shot = obs.get("screenshot")
                if not shot or not (self.mailbox / shot).exists():
                    raise ClientGone(f"observation {self.seq} has no screenshot: {obs.get('result')}")
                obs["frames"] = [f for f in obs.get("frames") or [] if (self.mailbox / f).exists()]
                return obs
            if not self.alive():
                raise ClientGone(f"the client exited during '{cmd['action']}'")
            s = self.status()
            if s and s.get("frame") != last_frame:
                last_frame, last_beat = s.get("frame"), time.monotonic()
            elif time.monotonic() - last_beat > self.heartbeat_timeout:
                raise ClientGone(f"the client stopped answering during '{cmd['action']}' (no heartbeat)")
            time.sleep(0.1)
        raise ClientGone(f"no observation for '{cmd['action']}' within {action_duration(cmd) + self.margin:.0f} s")


# ---------------------------------------------------------------------------- fake client (tool tests only)

class FakeClient:
    """Answers the mailbox like the controller would, without Unity. Used by --dry-run --fake-client and
    the tool's own tests; reports made with it say so."""

    def __init__(self, mailbox):
        self.mailbox = Path(mailbox)
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self.x, self.z, self.yaw = 0.0, -148.0, 0.0
        for sub in ("commands", "observations", "screenshots"):
            (self.mailbox / sub).mkdir(parents=True, exist_ok=True)

    def start(self):
        _write_atomic(self.mailbox / "status.json", {"state": "ready", "in_world": True, "busy": False,
                                                     "next_seq": 1, "frame": 1})
        self._thread.start()
        return self

    def alive(self):
        return self._thread.is_alive()

    def stop(self):
        self._stop.set()
        self._thread.join(timeout=5)

    def _loop(self):
        seq, frame = 1, 1
        while not self._stop.is_set():
            frame += 1
            try:
                _write_atomic(self.mailbox / "status.json", {"state": "ready", "in_world": True, "busy": False,
                                                             "next_seq": seq, "frame": frame})
            except OSError:
                pass  # the session is reading it (Windows will not replace an open file); the next beat follows
            path = self.mailbox / "commands" / f"{seq:04d}.json"
            if not path.exists():
                time.sleep(0.02)
                continue
            cmd = json.loads(path.read_text(encoding="utf-8"))
            result, moved = f"fake {cmd['action']}", 0.0
            if cmd["action"] == "move" and cmd.get("direction") == "forward":
                moved = 7.0 * float(cmd.get("seconds", 1))
                self.z += moved
            elif cmd["action"] == "teleport":
                self.x, self.z = cmd["x"], cmd["z"]
            fakes.tiny_png(self.mailbox / "screenshots" / f"{seq:04d}.png")
            obs = {"seq": seq, "action": cmd["action"], "ok": True, "result": result,
                   "screenshot": f"screenshots/{seq:04d}.png", "in_world": True, "moved": moved, "stuck": False,
                   "fps_avg": 120.0, "fps_min": 90.0, "events": [], "log_error_count": 0, "log_warning_count": 0,
                   "log_errors": [], "has_target": False, "loot_open": False, "dialog_open": False,
                   "player": {"name": "Fake", "level": 1, "health": 50, "max_health": 50, "dead": False,
                              "in_combat": False, "x": self.x, "y": 25.0, "z": self.z, "yaw": self.yaw},
                   "nearby": [{"name": "Mire Croaker", "kind": "creature", "reaction": "hostile", "level": 1,
                               "health_percent": 100, "distance": 20.0, "bearing": 10.0, "dead": False,
                               "lootable": False}]}
            _write_atomic(self.mailbox / "observations" / f"{seq:04d}.json", obs)
            seq += 1
            if cmd["action"] == "quit":
                return


def launch_client(exe, mailbox, log_path, realm, name, cls, resolution):
    width, height = resolution
    cmd = [exe, "-screen-fullscreen", "0", "-screen-width", str(width), "-screen-height", str(height),
           "-server", "127.0.0.1", "-httpport", str(realm["http_port"]), "-autoconnect", "-name", name,
           "-playtest", str(mailbox), "-logFile", str(log_path)]
    if cls:
        cmd += ["-class", cls]
    return proc.Background("client", cmd, Path(exe).parent, Path(str(log_path) + ".console.log"))
