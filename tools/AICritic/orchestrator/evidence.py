"""Evidence: manifest with hashes and timestamps, freshness checks, contact sheets for bursts and the
isolated copy the critic reads."""

import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # Pillow is optional: without it the critic gets the single frames.
    Image = None


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def iso(ts):
    return datetime.fromtimestamp(ts, timezone.utc).isoformat(timespec="seconds")


def file_entry(path, base):
    st = Path(path).stat()
    return {"path": Path(path).relative_to(base).as_posix(), "sha256": sha256(path), "bytes": st.st_size,
            "modified": iso(st.st_mtime), "mtime": st.st_mtime}


def contact_sheet(frames, out_path, columns=4, width=480):
    """A grid of burst frames, numbered in the corner. Returns the path or None without Pillow."""
    if Image is None or not frames:
        return None
    images = [Image.open(f).convert("RGB") for f in frames]
    w = width
    h = round(images[0].height * w / images[0].width)
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * w + (columns - 1) * 4, rows * h + (rows - 1) * 4), (20, 20, 20))
    draw = ImageDraw.Draw(sheet)
    for i, img in enumerate(images):
        x, y = (i % columns) * (w + 4), (i // columns) * (h + 4)
        sheet.paste(img.resize((w, h)), (x, y))
        draw.rectangle([x, y, x + 34, y + 18], fill=(0, 0, 0))
        draw.text((x + 5, y + 3), f"{i + 1:>2}", fill=(255, 255, 0))
    sheet.save(out_path)
    return out_path


def animated_gif(frames, out_path, ms_per_frame=250, width=800):
    if Image is None or len(frames) < 2:
        return None
    images = []
    for f in frames:
        img = Image.open(f).convert("RGB")
        images.append(img.resize((width, round(img.height * width / img.width))))
    images[0].save(out_path, save_all=True, append_images=images[1:], duration=ms_per_frame, loop=0)
    return out_path


def prepare_media(scene, round_dir):
    """Contact sheets and GIFs for every burst shot. Returns {shot name: {...}}."""
    shots_dir = Path(round_dir) / "evidence" / "screenshots"
    media = {}
    for shot in scene["shot"]:
        burst = int(shot.get("burst", 1))
        if burst > 1:
            frames = [shots_dir / f"{shot['name']}_{i}.png" for i in range(1, burst + 1)]
            frames = [f for f in frames if f.exists()]
            sheet = contact_sheet(frames, shots_dir / f"{shot['name']}_sheet.png")
            gif = animated_gif(frames, shots_dir / f"{shot['name']}.gif")
            media[shot["name"]] = {"frames": [f.name for f in frames],
                                   "sheet": Path(sheet).name if sheet else None,
                                   "gif": Path(gif).name if gif else None}
        else:
            f = shots_dir / f"{shot['name']}.png"
            media[shot["name"]] = {"frames": [f.name] if f.exists() else [], "sheet": None, "gif": None}
    return media


def critic_images(scene, media, max_images):
    """The pictures attached to the critic prompt: each single shot, each burst's contact sheet (or a
    few frames without Pillow), then the middle frame of each burst at full size while room is left."""
    picked, extra = [], []
    for shot in scene["shot"]:
        m = media.get(shot["name"], {})
        desc = shot.get("description", shot["name"])
        if m.get("sheet"):
            picked.append((m["sheet"], f"{shot['name']}: contact sheet of {len(m['frames'])} frames 0.25 s apart, "
                                       f"left to right, top to bottom. {desc}"))
            mid = m["frames"][len(m["frames"]) // 2]
            extra.append((mid, f"{shot['name']}: frame {len(m['frames']) // 2 + 1} at full size. {desc}"))
        elif len(m.get("frames", [])) > 1:
            for i in (0, len(m["frames"]) // 2, len(m["frames"]) - 1):
                picked.append((m["frames"][i], f"{shot['name']}: frame {i + 1} of {len(m['frames'])}. {desc}"))
        elif m.get("frames"):
            picked.append((m["frames"][0], f"{shot['name']}: {desc}"))
    for item in extra:
        if len(picked) >= max_images:
            break
        picked.append(item)
    return picked[:max_images]


def build_manifest(round_dir, context):
    """Hashes every evidence file and records the build identity. context carries the build/test/capture
    results; it is stored as-is."""
    evidence = Path(round_dir) / "evidence"
    files = [file_entry(p, evidence) for p in sorted(evidence.rglob("*")) if p.is_file() and p.name != "manifest.json"]
    manifest = {"generated": datetime.now(timezone.utc).isoformat(timespec="seconds"), "files": files, **context}
    (evidence / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def freshness_problems(manifest, round_started, build_finished):
    """Screenshots must be newer than this round's client build, which must be newer than the round's
    start. Anything else would be an old picture standing in for the current build."""
    problems = []
    if build_finished is None or build_finished < round_started:
        problems.append("the client build is not from this round")
    pictures = [f for f in manifest["files"] if f["path"].endswith(".png")
                and (f["path"].startswith("screenshots/") or "/screenshots/" in f["path"])]
    for f in pictures:
        if build_finished is None or f["mtime"] < build_finished:
            problems.append(f"{f['path']} is older than this round's client build")
    if not pictures:
        problems.append("no screenshots were captured")
    return problems


def stage_for_critic(round_dir, stage_dir, extra_files):
    """Copies the evidence (and extra files such as the diff and the task) into a fresh folder outside
    every Git repository. The critic works only there."""
    stage = Path(stage_dir)
    if stage.exists():
        shutil.rmtree(stage)
    shutil.copytree(Path(round_dir) / "evidence", stage)
    for name, src in extra_files.items():
        shutil.copy2(src, stage / name)
    return stage


def folder_fingerprint(folder):
    h = hashlib.sha256()
    for p in sorted(Path(folder).rglob("*")):
        if p.is_file():
            h.update(p.relative_to(folder).as_posix().encode())
            h.update(sha256(p).encode())
    return h.hexdigest()
