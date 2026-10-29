"""Turns a CI screen recording (build/menu-check.ps1) into a video, each frame labelled with
the step it's in.

Frames are named by milliseconds since the start and shown for as long as they really lasted,
so the video plays at the speed things happened. For .github/workflows/menu-check.yml.

Usage: python frames-video.py <out dir> <video.mp4> <title>
"""
import os
import subprocess
import sys
import tempfile

import imageio_ffmpeg
from PIL import Image, ImageDraw

src, video, title = sys.argv[1], sys.argv[2], sys.argv[3]

frames = sorted(
    (int(name[:-4]), os.path.join(src, "frames", name))
    for name in os.listdir(os.path.join(src, "frames"))
    if name.endswith(".jpg")
)
events = []
with open(os.path.join(src, "events.txt"), encoding="utf-8-sig") as f:
    for line in f:
        ms, text = line.rstrip("\n").split("\t", 1)
        events.append((int(ms), text))


def label(ms):
    """The latest step at this time, and how long since it began."""
    current = None
    for at, text in events:
        if at <= ms:
            current = (at, text)
    if current is None:
        return title
    return f"{title}   |   {current[1]}  +{ms - current[0]} ms"


with tempfile.TemporaryDirectory() as tmp:
    entries = []
    for index, (ms, path) in enumerate(frames):
        image = Image.open(path).convert("RGB")
        # Even sizes, which H.264 needs.
        image = image.crop((0, 0, image.width // 2 * 2, image.height // 2 * 2))
        draw = ImageDraw.Draw(image)
        draw.rectangle((0, 0, image.width, 26), fill="black")
        draw.text((8, 7), label(ms), fill="white")
        out = os.path.join(tmp, f"{index:05d}.png")
        image.save(out)
        following = frames[index + 1][0] if index + 1 < len(frames) else ms + 40
        entries.append((out, max(1, following - ms) / 1000))

    listing = os.path.join(tmp, "frames.txt")
    with open(listing, "w", encoding="utf-8") as f:
        for path, seconds in entries:
            f.write(f"file '{path}'\nduration {seconds:.4f}\n")
        f.write(f"file '{entries[-1][0]}'\n")

    subprocess.run(
        [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
         "-f", "concat", "-safe", "0", "-i", listing,
         "-vsync", "vfr", "-pix_fmt", "yuv420p", "-c:v", "libx264", "-crf", "23",
         "-movflags", "+faststart", video],
        check=True,
    )
print(video, len(frames), "frames")
