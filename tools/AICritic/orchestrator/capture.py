"""The scripted play session: a fresh in-memory realm, one client run per camera shot, an optional bot
load phase. Only the client's and server's existing command-line flags are used."""

import re
import socket
import string
import time
import urllib.error
import urllib.request
from pathlib import Path

from . import proc
from .pipeline import client_exe, now_iso


def port_free(port, udp=False):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM if udp else socket.SOCK_STREAM)
    try:
        s.bind(("127.0.0.1", port))
        return True
    except OSError:
        return False
    finally:
        s.close()


def api_ready(http_port):
    """Login is POST-only: a 405 on GET means the account API is listening."""
    try:
        urllib.request.urlopen(f"http://127.0.0.1:{http_port}/api/v1/login", timeout=2)
        return False
    except urllib.error.HTTPError as e:
        return e.code == 405
    except (urllib.error.URLError, OSError):
        return False


def start_realm(cfg, wt, logs):
    realm = cfg["realm"]
    for port, udp in ((realm["port"], True), (realm["http_port"], False)):
        if not port_free(port, udp):
            raise RuntimeError(f"port {port} ({'UDP' if udp else 'TCP'}) is in use: stop whatever uses it "
                               f"or change [realm] in the config")
    cmd = ["dotnet", "run", "--project", "server/Madlands.Server", "-c", "Debug", "--no-build", "--",
           "--bind", "127.0.0.1", "--db", "memory", "--gm-all", "--api-rate-limit", "0",
           "--port", str(realm["port"]), "--http-port", str(realm["http_port"]),
           "--seed", str(realm["seed"]), "--stats-interval", str(realm["stats_interval"])]
    server = proc.Background("realm", cmd, wt, logs / "server.log", env=proc.clean_env())
    deadline = time.monotonic() + cfg["timeouts"]["server_start"]
    while time.monotonic() < deadline:
        if not server.alive():
            server.stop()
            raise RuntimeError(f"the server exited during startup (see {server.log_path})")
        if api_ready(realm["http_port"]):
            return server
        time.sleep(0.5)
    server.stop()
    raise RuntimeError(f"the account API did not answer within {cfg['timeouts']['server_start']} s")


def character_name(shot_name, salt):
    """Letters only, 2-16 characters (NameRules), unique per shot and round."""
    letters = "".join(c for c in shot_name.lower() if c in string.ascii_lowercase)
    return ("Ai" + salt + letters)[:16].capitalize()


def shot_files(out_dir, shot):
    burst = int(shot.get("burst", 1))
    if burst > 1:
        return [Path(out_dir) / f"{shot['name']}_{i}.png" for i in range(1, burst + 1)]
    return [Path(out_dir) / f"{shot['name']}.png"]


def run_shot(cfg, scene, wt, shot, shots_dir, logs, salt):
    realm = cfg["realm"]
    width, height = scene.get("resolution", [1600, 900])
    burst = int(shot.get("burst", 1))
    delay = float(shot.get("delay", 7))
    quit_after = float(shot.get("quit_after", delay + 0.25 * burst + 9))
    target = Path(shots_dir) / f"{shot['name']}.png"
    for f in shot_files(shots_dir, shot):
        if f.exists():
            f.unlink()
    cmd = [client_exe(wt), "-screen-fullscreen", "0", "-screen-width", str(width), "-screen-height", str(height),
           "-server", "127.0.0.1", "-httpport", str(realm["http_port"]),
           "-autoconnect", "-name", character_name(shot["name"], salt)]
    if shot.get("class"):
        cmd += ["-class", shot["class"]]
    if shot.get("exec"):
        cmd += ["-exec", ";".join(shot["exec"])]
    cmd += [str(a) for a in shot.get("args", [])]
    cmd += ["-screenshot", target, "-screenshotdelay", f"{delay:g}", "-quitafter", f"{quit_after:g}"]
    if burst > 1:
        cmd += ["-screenshotburst", str(burst)]
    player_log = logs / f"client-{shot['name']}.log"
    cmd += ["-logFile", player_log]
    r = proc.run(f"shot_{shot['name']}", cmd, Path(client_exe(wt)).parent, logs / f"client-{shot['name']}.console.log",
                 quit_after + cfg["timeouts"]["client_margin"], sample_metrics=True)
    r.log = str(player_log)
    files = shot_files(shots_dir, shot)
    missing = [f.name for f in files if not f.exists() or f.stat().st_size == 0]
    result = r.to_dict()
    result.update({"shot": shot["name"], "files": [f.name for f in files if f.exists()], "missing": missing,
                   "client_log": client_log_summary(player_log), "finished": now_iso()})
    # Unity players often exit with a non-zero code after Application.Quit on Windows; the pictures and
    # the log decide whether the shot worked.
    result["ok"] = not missing and not r.timed_out
    if missing:
        result["error"] = f"missing screenshots: {', '.join(missing)}"
    return result


_SOCKET_TROUBLE = re.compile(r"(SocketException|Bind exception|Could not start the network socket|"
                            r"buffer space|address already in use|Only one usage of each socket address)", re.I)


def infrastructure_failure(shot_result):
    """True when a shot failed before entering the world because of a socket error, not because of the game."""
    log = shot_result.get("client_log") or {}
    if log.get("entered_world"):
        return False
    return any(_SOCKET_TROUBLE.search(line) for line in log.get("first_problems", []))


def run_bots(cfg, wt, logs):
    realm = cfg["realm"]
    if realm["bots"] <= 0:
        return None
    cmd = ["dotnet", "run", "--project", "server/Madlands.Bots", "-c", "Debug", "--no-build", "--",
           "--host", "127.0.0.1", "--http-port", str(realm["http_port"]),
           "--count", str(realm["bots"]), "--seconds", str(realm["bot_seconds"])]
    r = proc.run("bots", cmd, wt, logs / "bots.log", realm["bot_seconds"] + cfg["timeouts"]["bots"],
                 env=proc.clean_env())
    d = r.to_dict()
    d["bots"] = realm["bots"]
    d["seconds_requested"] = realm["bot_seconds"]
    return d


# ---------------------------------------------------------------------------- log summaries

_STATS = re.compile(r"Tick avg (?P<avg>[\d.]+) ms, p99 (?P<p99>[\d.]+) ms, max (?P<max>[\d.]+) ms \| sessions "
                    r"(?P<sessions>\d+), players (?P<players>\d+) \| out (?P<out>[\d.]+) KB/s, in (?P<inn>[\d.]+) KB/s")
_LEVEL = re.compile(r"\[\d{2}:\d{2}:\d{2}\s+(?P<lvl>VRB|DBG|INF|WRN|ERR|FTL)\]")


def server_log_summary(path):
    stats, counts, problems = [], {"WRN": 0, "ERR": 0, "FTL": 0}, []
    try:
        lines = Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return {"missing": True}
    for line in lines:
        m = _STATS.search(line)
        if m:
            stats.append({k: float(v) for k, v in m.groupdict().items()})
        lvl = _LEVEL.search(line)
        if lvl and lvl.group("lvl") in counts:
            counts[lvl.group("lvl")] += 1
            if len(problems) < 25:
                problems.append(line.strip()[:300])
        elif ("Exception" in line or "Unhandled" in line) and len(problems) < 25:
            problems.append(line.strip()[:300])
    loaded = [s for s in stats if s["players"] > 1]
    return {
        "stats_lines": len(stats),
        "tick_avg_ms_max": max((s["avg"] for s in stats), default=None),
        "tick_p99_ms_max": max((s["p99"] for s in stats), default=None),
        "tick_max_ms": max((s["max"] for s in stats), default=None),
        "max_players": max((int(s["players"]) for s in stats), default=0),
        "under_load": {
            "samples": len(loaded),
            "tick_avg_ms_max": max((s["avg"] for s in loaded), default=None),
            "tick_p99_ms_max": max((s["p99"] for s in loaded), default=None),
            "out_kb_s_max": max((s["out"] for s in loaded), default=None),
        },
        "level_counts": counts,
        "problem_lines": problems,
        "stats": stats[-40:],
    }


_CLIENT_PROBLEM = re.compile(r"(Exception|NullReference|Error|error|Script error|Failed|failed)")
_CLIENT_NOISE = re.compile(r"(Fallback handler could not load|d3d12: profiler|Unable to find player assembly|"
                           r"\[Physics::Module\])")


def client_log_summary(path):
    try:
        lines = Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return {"missing": True}
    problems = [l.strip()[:300] for l in lines if _CLIENT_PROBLEM.search(l) and not _CLIENT_NOISE.search(l)]
    exceptions = [l for l in problems if "Exception" in l]
    return {"lines": len(lines), "problem_lines": len(problems), "exceptions": len(exceptions),
            "first_problems": problems[:15],
            "entered_world": any("Screenshot saved" in l for l in lines)}


def run_capture(cfg, scene, wt, round_dir, salt):
    """Returns (ok, result dict, failure text)."""
    evidence = Path(round_dir) / "evidence"
    logs, shots_dir = evidence / "logs", evidence / "screenshots"
    logs.mkdir(parents=True, exist_ok=True)
    shots_dir.mkdir(parents=True, exist_ok=True)
    result = {"started": now_iso(), "shots": [], "bots": None, "server": None}
    try:
        server = start_realm(cfg, wt, logs)
    except RuntimeError as e:
        result["server"] = server_log_summary(logs / "server.log")
        return False, result, f"realm: {e}"
    failure = None
    try:
        for shot in scene["shot"]:
            s = run_shot(cfg, scene, wt, shot, shots_dir, logs, salt)
            if not s["ok"] and infrastructure_failure(s):
                # The client never got into the world because the OS refused it a socket (seen as WSAENOBUFS
                # right after a bot run): try that shot once more, keeping the first attempt's log.
                first = logs / f"client-{shot['name']}.log"
                if first.exists():
                    first.replace(logs / f"client-{shot['name']}.attempt1.log")
                time.sleep(5)
                reason = s["client_log"].get("first_problems", ["?"])[0]
                s = run_shot(cfg, scene, wt, shot, shots_dir, logs, salt + "r")
                s["retried_after"] = reason
            result["shots"].append(s)
            if not s["ok"] and failure is None:
                failure = f"shot {shot['name']}: {s.get('error') or 'failed'}"
            if not server.alive():
                failure = failure or "the server stopped during the play session"
                break
        if failure is None and server.alive():
            result["bots"] = run_bots(cfg, wt, logs)
            # One more stats interval so the last load numbers reach the log.
            time.sleep(cfg["realm"]["stats_interval"] + 1)
        if failure is None and not server.alive():
            failure = "the server stopped during the bot phase"
    finally:
        server.stop()
    result["server"] = server_log_summary(logs / "server.log")
    result["finished"] = now_iso()
    return failure is None, result, failure
