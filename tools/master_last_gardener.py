"""QC, normalize, and master THE LAST GARDENER into a 1080p feature file."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from pathlib import Path


SHOT_COUNT = 60
MIN_SHOT_DURATION = 14.8
MIN_MASTER_DURATION = 900.0


def run(command: list[str], *, capture: bool = False) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, check=True, text=True, capture_output=capture)


def probe(path: Path) -> dict:
    result = run(
        [
            "ffprobe", "-v", "error", "-show_entries",
            "format=duration,size:stream=index,codec_name,codec_type,width,height,r_frame_rate,sample_rate,channels",
            "-of", "json", str(path),
        ],
        capture=True,
    )
    return json.loads(result.stdout)


def qc(path: Path) -> dict:
    info = probe(path)
    duration = float(info["format"]["duration"])
    video = next((s for s in info["streams"] if s.get("codec_type") == "video"), None)
    audio = next((s for s in info["streams"] if s.get("codec_type") == "audio"), None)
    if duration < MIN_SHOT_DURATION or not video or not audio:
        raise RuntimeError(f"basic QC failed for {path}: {info}")

    freeze = run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-vf", "freezedetect=n=-50dB:d=2", "-an", "-f", "null", "-"],
        capture=True,
    ).stderr
    freeze_durations = [float(x) for x in re.findall(r"freeze_duration:\s*([0-9.]+)", freeze)]
    if freeze_durations and max(freeze_durations) > 8.0:
        raise RuntimeError(f"frozen-motion QC failed for {path}: {max(freeze_durations):.2f}s")

    volume = run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-vn", "-af", "volumedetect", "-f", "null", "-"],
        capture=True,
    ).stderr
    match = re.search(r"mean_volume:\s*(-?inf|-?[0-9.]+) dB", volume)
    if not match or match.group(1) == "-inf" or float(match.group(1)) < -55:
        raise RuntimeError(f"silent-audio QC failed for {path}")
    return {"path": str(path), "duration": duration, "freeze_max": max(freeze_durations, default=0.0), "mean_volume": float(match.group(1))}


def discover(source: Path) -> list[Path]:
    files = sorted(source.rglob("*.mp4"))
    numbered: dict[int, Path] = {}
    for path in files:
        match = re.search(r"(?:^|[/\\])(\d{2})_", str(path))
        if match:
            numbered[int(match.group(1))] = path
    missing = [n for n in range(1, SHOT_COUNT + 1) if n not in numbered]
    if missing:
        raise RuntimeError(f"missing generated shots: {missing}")
    return [numbered[n] for n in range(1, SHOT_COUNT + 1)]


def master(source: Path, output: Path, work: Path) -> None:
    shots = discover(source)
    work.mkdir(parents=True, exist_ok=True)
    normalized = work / "normalized"
    normalized.mkdir(exist_ok=True)
    report = []
    intermediates = []
    for index, shot in enumerate(shots, 1):
        report.append(qc(shot))
        target = normalized / f"{index:02d}.mp4"
        run(
            [
                "ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", str(shot),
                "-vf", "scale=1920:1080:flags=lanczos,format=yuv420p",
                "-r", "24", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
                "-c:a", "aac", "-b:a", "256k", "-ar", "48000", "-ac", "2",
                "-movflags", "+faststart", str(target),
            ]
        )
        intermediates.append(target)

    concat_file = work / "concat.txt"
    concat_file.write_text("".join(f"file '{p.as_posix()}'\n" for p in intermediates), encoding="utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    run(
        [
            "ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0",
            "-i", str(concat_file), "-c", "copy", "-movflags", "+faststart", str(output),
        ]
    )
    final = probe(output)
    duration = float(final["format"]["duration"])
    video = next(s for s in final["streams"] if s.get("codec_type") == "video")
    audio = next(s for s in final["streams"] if s.get("codec_type") == "audio")
    if duration < MIN_MASTER_DURATION:
        raise RuntimeError(f"master is shorter than 15 minutes: {duration:.3f}s")
    if (video.get("width"), video.get("height"), video.get("r_frame_rate")) != (1920, 1080, "24/1"):
        raise RuntimeError(f"master video format is wrong: {video}")
    if int(audio.get("channels", 0)) != 2:
        raise RuntimeError(f"master audio is not stereo: {audio}")
    (work / "qc-report.json").write_text(json.dumps({"shots": report, "master": final}, indent=2), encoding="utf-8")
    print("MASTER_VERIFIED", output, f"{duration:.3f}s", "1920x1080", "24fps", "stereo")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    args = parser.parse_args()
    master(args.source, args.output, args.work)


if __name__ == "__main__":
    main()
