"""Git plumbing: worktrees, content snapshots without commits, diffs and the approved commit."""

import hashlib
import os
import subprocess
from pathlib import Path


class GitError(Exception):
    pass


def git(args, cwd, env=None, check=True, binary=False):
    p = subprocess.run(["git", *args], cwd=str(cwd), env=env, capture_output=True)
    out = p.stdout if binary else p.stdout.decode("utf-8", errors="replace")
    if check and p.returncode != 0:
        raise GitError(f"git {' '.join(args)} failed ({p.returncode}): "
                       f"{p.stderr.decode('utf-8', errors='replace').strip()}")
    return out if binary else out.strip()


def rev(cwd, ref="HEAD"):
    return git(["rev-parse", ref], cwd)


def branch(cwd):
    return git(["rev-parse", "--abbrev-ref", "HEAD"], cwd)


def branch_exists(repo, name):
    return subprocess.run(["git", "show-ref", "--verify", "--quiet", f"refs/heads/{name}"],
                          cwd=str(repo)).returncode == 0


def add_worktree(repo, path, base_ref, new_branch=None):
    path = Path(path)
    if path.exists():
        raise GitError(f"worktree path already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    if new_branch:
        if branch_exists(repo, new_branch):
            raise GitError(f"branch {new_branch} already exists")
        git(["worktree", "add", "-b", new_branch, str(path), base_ref], repo)
    else:
        git(["worktree", "add", "--detach", str(path), base_ref], repo)


def remove_worktree(repo, path):
    git(["worktree", "remove", "--force", str(path)], repo)


def delete_branch(repo, name):
    git(["branch", "-D", name], repo)


def snapshot(cwd, index_file):
    """Tree id of the working tree's current content (tracked + untracked, minus ignored), without
    touching the real index, HEAD or any ref."""
    env = {**os.environ, "GIT_INDEX_FILE": str(index_file)}
    git(["read-tree", "HEAD"], cwd, env=env)
    git(["add", "-A"], cwd, env=env)
    tree = git(["write-tree"], cwd, env=env)
    try:
        os.remove(index_file)
    except OSError:
        pass
    return tree


def head_tree(cwd):
    return git(["rev-parse", "HEAD^{tree}"], cwd)


def diff(cwd, a, b, out_path=None):
    text = git(["diff", "--no-color", "--find-renames", a, b], cwd)
    if out_path:
        Path(out_path).write_text(text + ("\n" if text else ""), encoding="utf-8")
    return text


def diff_stat(cwd, a, b):
    return git(["diff", "--stat", "--find-renames", a, b], cwd)


def name_status(cwd, a, b):
    """[(status letter, path)] between two trees (renames are reported as delete + add)."""
    out = git(["diff", "--name-status", "--no-renames", a, b], cwd)
    result = []
    for line in out.splitlines():
        if not line.strip():
            continue
        status, _, path = line.partition("\t")
        result.append((status[0], path))
    return result


def restore_paths(cwd, tree, entries):
    """Puts the given (status, path) changes back to how they are in tree."""
    for status, path in entries:
        full = Path(cwd) / path
        if status == "A":
            if full.is_file():
                full.unlink()
        else:
            git(["restore", f"--source={tree}", "--worktree", "--", path], cwd)


def state_fingerprint(cwd):
    """A hash of HEAD, the branch and the full content of every non-ignored file."""
    tree = snapshot(cwd, temp_index(cwd, "fingerprint"))
    head, br = rev(cwd), branch(cwd)
    h = hashlib.sha256(f"{head}|{br}|{tree}".encode()).hexdigest()
    return {"head": head, "branch": br, "tree": tree, "hash": h}


def temp_index(cwd, name):
    """A temporary index file inside the repository's git dir (never inside the working tree)."""
    gitdir = Path(cwd, git(["rev-parse", "--git-dir"], cwd))
    return gitdir / f"aicritic-{name}.idx"


def commit_all(cwd, message):
    git(["add", "-A"], cwd)
    git(["commit", "-m", message], cwd)
    return rev(cwd)
