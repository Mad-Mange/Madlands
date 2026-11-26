"""Subprocesses with timeouts, process-tree kills, log files and (on Windows) CPU/memory sampling."""

import os
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

IS_WINDOWS = os.name == "nt"

# Variables a parent Claude Code session sets for its own tools. A builder started from inside such a
# session must look like an ordinary, independent session.
_CLAUDE_KEEP = {"CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_OAUTH_TOKEN",
                "CLAUDE_CODE_GIT_BASH_PATH", "CLAUDE_CODE_MAX_OUTPUT_TOKENS", "CLAUDE_CONFIG_DIR"}


def clean_env(extra=None):
    env = {}
    for key, value in os.environ.items():
        upper = key.upper()
        if upper == "CLAUDECODE" or upper in ("CLAUDE_PID", "CLAUDE_EFFORT"):
            continue
        if upper.startswith("CLAUDE_CODE_") and upper not in _CLAUDE_KEEP:
            continue
        env[key] = value
    if extra:
        env.update(extra)
    return env


def which(name):
    return shutil.which(name)


@dataclass
class ProcResult:
    name: str
    cmd: list
    returncode: int | None
    timed_out: bool
    seconds: float
    log: str
    metrics: dict = field(default_factory=dict)
    error: str = ""

    @property
    def ok(self):
        return self.returncode == 0 and not self.timed_out and not self.error

    def to_dict(self):
        return {"name": self.name, "cmd": [str(c) for c in self.cmd], "returncode": self.returncode,
                "timed_out": self.timed_out, "seconds": round(self.seconds, 1), "log": self.log,
                "metrics": self.metrics, "error": self.error, "ok": self.ok}


def kill_tree(pid):
    if IS_WINDOWS:
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    else:
        try:
            os.killpg(os.getpgid(pid), 9)
        except (ProcessLookupError, PermissionError):
            pass


def _popen_kwargs():
    if IS_WINDOWS:
        return {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP}
    return {"start_new_session": True}


def cmdline(cmd):
    return subprocess.list2cmdline([str(c) for c in cmd]) if IS_WINDOWS else " ".join(str(c) for c in cmd)


def run(name, cmd, cwd, log_path, timeout, env=None, stdin_path=None, stdout_path=None,
        sample_metrics=False):
    """Runs cmd to completion. stderr (and stdout unless stdout_path is given) goes to log_path."""
    log_path = Path(log_path)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    sampler = _Sampler() if sample_metrics and IS_WINDOWS else None
    with open(log_path, "w", encoding="utf-8", errors="replace") as log:
        log.write(f"$ {cmdline(cmd)}\n# cwd: {cwd}\n\n")
        log.flush()
        stdin = open(stdin_path, "rb") if stdin_path else subprocess.DEVNULL
        out = open(stdout_path, "wb") if stdout_path else log
        try:
            try:
                p = subprocess.Popen([str(c) for c in cmd], cwd=str(cwd), stdin=stdin, stdout=out,
                                     stderr=log, env=env, **_popen_kwargs())
            except OSError as e:
                log.write(f"\n[aicritic] could not start: {e}\n")
                return ProcResult(name, cmd, None, False, 0.0, str(log_path), error=f"could not start: {e}")
            timed_out = False
            deadline = start + timeout
            while True:
                try:
                    rc = p.wait(timeout=0.5)
                    break
                except subprocess.TimeoutExpired:
                    if sampler:
                        sampler.sample(p)
                    if time.monotonic() > deadline:
                        timed_out = True
                        kill_tree(p.pid)
                        rc = p.wait()
                        break
            metrics = sampler.finish(p, time.monotonic() - start) if sampler else {}
        finally:
            if stdin_path:
                stdin.close()
            if stdout_path:
                out.close()
        seconds = time.monotonic() - start
        log.write(f"\n[aicritic] exit {rc}{' (timed out, killed)' if timed_out else ''} after {seconds:.1f} s\n")
    return ProcResult(name, cmd, rc, timed_out, seconds, str(log_path), metrics,
                      error=f"timed out after {timeout:.0f} s" if timed_out else "")


class Background:
    """A long-running child (the test realm). stop() kills the whole tree."""

    def __init__(self, name, cmd, cwd, log_path, env=None):
        self.name = name
        self.log_path = Path(log_path)
        self.log_path.parent.mkdir(parents=True, exist_ok=True)
        self._log = open(self.log_path, "w", encoding="utf-8", errors="replace")
        self._log.write(f"$ {cmdline(cmd)}\n# cwd: {cwd}\n\n")
        self._log.flush()
        self.proc = subprocess.Popen([str(c) for c in cmd], cwd=str(cwd), stdin=subprocess.DEVNULL,
                                     stdout=self._log, stderr=subprocess.STDOUT, env=env, **_popen_kwargs())

    def alive(self):
        return self.proc.poll() is None

    def stop(self):
        if self.alive():
            kill_tree(self.proc.pid)
            try:
                self.proc.wait(timeout=30)
            except subprocess.TimeoutExpired:
                pass
        self._log.close()


class _Sampler:
    """Peak working set and CPU time of one Windows process, read through the Win32 API."""

    def __init__(self):
        import ctypes
        from ctypes import wintypes

        class PMC(ctypes.Structure):
            _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
                        ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t), ("QuotaPagedPoolUsage", ctypes.c_size_t),
                        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t), ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                        ("PagefileUsage", ctypes.c_size_t), ("PeakPagefileUsage", ctypes.c_size_t)]

        self._ctypes, self._wintypes, self._PMC = ctypes, wintypes, PMC
        self._psapi = ctypes.WinDLL("psapi")
        self._kernel = ctypes.WinDLL("kernel32")
        self._psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(PMC), wintypes.DWORD]
        self._kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
        self.peak_ws = 0
        self.samples = 0

    def sample(self, p):
        handle = getattr(p, "_handle", None)
        if handle is None:
            return
        c = self._PMC()
        c.cb = self._ctypes.sizeof(c)
        if self._psapi.GetProcessMemoryInfo(int(handle), self._ctypes.byref(c), c.cb):
            self.peak_ws = max(self.peak_ws, c.PeakWorkingSetSize)
            self.samples += 1

    def finish(self, p, wall):
        handle = getattr(p, "_handle", None)
        cpu = None
        if handle is not None:
            ft = [self._wintypes.FILETIME() for _ in range(4)]
            if self._kernel.GetProcessTimes(int(handle), *[self._ctypes.byref(f) for f in ft]):
                def secs(f):
                    return ((f.dwHighDateTime << 32) | f.dwLowDateTime) / 1e7
                cpu = secs(ft[2]) + secs(ft[3])
        cores = os.cpu_count() or 1
        return {
            "peak_working_set_mb": round(self.peak_ws / 2**20, 1) if self.peak_ws else None,
            "cpu_seconds": round(cpu, 2) if cpu is not None else None,
            "avg_cpu_percent_of_machine": round(100 * cpu / wall / cores, 1) if cpu and wall > 0 else None,
            "logical_cores": cores,
            "memory_samples": self.samples,
        }


def python_exe():
    return sys.executable
