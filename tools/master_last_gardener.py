"""Strictly QC, normalize, and master THE LAST GARDENER as a 1080p feature."""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import subprocess
import uuid
from fractions import Fraction
from pathlib import Path
from typing import Any


SHOT_COUNT = 60
SOURCE_WIDTH = 1344
SOURCE_HEIGHT = 768
SOURCE_FRAMES = 362
MASTER_WIDTH = 1920
MASTER_HEIGHT = 1080
MASTER_FPS = 24
MASTER_AUDIO_RATE = 48_000
MIN_SHOT_DURATION = 14.8
MIN_MASTER_DURATION = 900.0
MIN_INTEGRATED_LUFS = -55.0
NUMBERED_NAME = re.compile(r"^(\d{2})(?:_|[-. ])")


def run(command: list[str], *, capture: bool = False) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, check=True, text=True, capture_output=capture)


def probe(path: Path, *, count_frames: bool = False) -> dict[str, Any]:
    command = ["ffprobe", "-v", "error"]
    if count_frames:
        command.append("-count_frames")
    command.extend(
        [
            "-show_entries",
            "format=duration,size,start_time:"
            "stream=index,codec_name,codec_type,width,height,pix_fmt,r_frame_rate,"
            "avg_frame_rate,nb_frames,nb_read_frames,sample_rate,channels,channel_layout,"
            "duration,start_time,time_base",
            "-of",
            "json",
            str(path),
        ]
    )
    result = run(command, capture=True)
    return json.loads(result.stdout)


def _load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(f"cannot read JSON metadata {path}: {error}") from error


def _number_from_label(label: str) -> int:
    match = NUMBERED_NAME.match(label)
    if not match:
        raise RuntimeError(f"shot label must start with a two-digit number: {label!r}")
    number = int(match.group(1))
    if not 1 <= number <= SHOT_COUNT:
        raise RuntimeError(f"shot number outside 01..{SHOT_COUNT}: {label!r}")
    return number


def _resolve_record_path(raw: str, metadata_dir: Path, source: Path) -> Path:
    candidate = Path(raw).expanduser()
    candidates = [candidate] if candidate.is_absolute() else [metadata_dir / candidate, source / candidate]
    for path in candidates:
        resolved = path.resolve()
        if resolved.is_file():
            return resolved
    raise RuntimeError(f"metadata video path does not exist: {raw!r}")


def _manifest_records(path: Path) -> list[dict[str, Any]]:
    payload = _load_json(path)
    records = payload.get("shots") if isinstance(payload, dict) else payload
    if not isinstance(records, list):
        raise RuntimeError(f"manifest must be a list or contain a shots list: {path}")
    if len(records) != SHOT_COUNT:
        raise RuntimeError(f"manifest must contain exactly {SHOT_COUNT} shots, found {len(records)}: {path}")
    result: list[dict[str, Any]] = []
    seen_numbers: set[int] = set()
    seen_slugs: set[str] = set()
    for record in records:
        if not isinstance(record, dict):
            raise RuntimeError(f"manifest entries must be objects: {path}")
        try:
            number = int(record["number"])
            slug = str(record["slug"])
        except (KeyError, TypeError, ValueError) as error:
            raise RuntimeError(f"manifest entry needs integer number and string slug: {record}") from error
        if number in seen_numbers or slug in seen_slugs:
            raise RuntimeError(f"duplicate manifest number or slug: number={number}, slug={slug!r}")
        if _number_from_label(slug) != number:
            raise RuntimeError(f"manifest number/slug mismatch: number={number}, slug={slug!r}")
        seen_numbers.add(number)
        seen_slugs.add(slug)
        result.append(record)
    expected = set(range(1, SHOT_COUNT + 1))
    if seen_numbers != expected:
        raise RuntimeError(f"manifest shot numbers must be exactly 01..{SHOT_COUNT}")
    return sorted(result, key=lambda item: int(item["number"]))


def _state_completed(path: Path) -> dict[str, str]:
    payload = _load_json(path)
    completed = payload.get("completed") if isinstance(payload, dict) else None
    if not isinstance(completed, dict):
        raise RuntimeError(f"state must contain a completed object: {path}")
    if len(completed) != SHOT_COUNT:
        raise RuntimeError(f"state.completed must contain exactly {SHOT_COUNT} shots, found {len(completed)}: {path}")
    normalized: dict[str, str] = {}
    for key, value in completed.items():
        if not isinstance(key, str) or not key:
            raise RuntimeError(f"state.completed keys must be non-empty strings: {path}")
        # Generator state v2 stores a verification record; legacy state stored
        # the output path directly. Mastering accepts both and independently
        # re-runs all media QC below.
        raw_path = value.get("path") if isinstance(value, dict) else value
        if not isinstance(raw_path, str) or not raw_path:
            raise RuntimeError(f"state.completed[{key!r}] has no output path: {path}")
        normalized[key] = raw_path
    return normalized


def _validate_exact_paths(numbered: dict[int, list[Path]], *, origin: str) -> list[Path]:
    duplicates = {number: paths for number, paths in numbered.items() if len(paths) != 1}
    missing = [number for number in range(1, SHOT_COUNT + 1) if number not in numbered]
    extras = sorted(number for number in numbered if number not in range(1, SHOT_COUNT + 1))
    if duplicates or missing or extras:
        duplicate_text = {number: [str(path) for path in paths] for number, paths in duplicates.items()}
        raise RuntimeError(
            f"{origin} must resolve exactly one file for each shot 01..{SHOT_COUNT}; "
            f"missing={missing}, extras={extras}, duplicates={duplicate_text}"
        )
    ordered = [numbered[number][0].resolve() for number in range(1, SHOT_COUNT + 1)]
    aliases: dict[Path, list[int]] = {}
    for number, path in enumerate(ordered, 1):
        aliases.setdefault(path, []).append(number)
    reused = {str(path): numbers for path, numbers in aliases.items() if len(numbers) > 1}
    if reused:
        raise RuntimeError(f"the same input file is assigned to multiple shots: {reused}")
    return ordered


def discover(source: Path, *, manifest: Path | None = None, state: Path | None = None,
             exclude: tuple[Path, ...] = ()) -> list[Path]:
    source = source.resolve()
    manifest_was_explicit = manifest is not None
    manifest = manifest.resolve() if manifest else ((source / "manifest.json") if (source / "manifest.json").is_file() else None)
    state = state.resolve() if state else ((source / "state.json") if (source / "state.json").is_file() else None)

    # Batch generation rewrites manifest.json for only the latest range, while
    # state.completed accumulates all finished shots. A complete state is the
    # stronger automatic SSOT; an explicitly supplied manifest is always strict.
    records = _manifest_records(manifest) if manifest and (manifest_was_explicit or state is None) else None
    completed = _state_completed(state) if state else None
    numbered: dict[int, list[Path]] = {}

    if records is not None and completed is not None:
        manifest_slugs = {str(record["slug"]) for record in records}
        state_slugs = set(completed)
        if manifest_slugs != state_slugs:
            raise RuntimeError(
                "manifest/state shot sets differ; "
                f"missing in state={sorted(manifest_slugs - state_slugs)}, "
                f"extra in state={sorted(state_slugs - manifest_slugs)}"
            )
        for record in records:
            number = int(record["number"])
            slug = str(record["slug"])
            numbered[number] = [_resolve_record_path(completed[slug], state.parent, source)]
        return _validate_exact_paths(numbered, origin="manifest/state")

    if completed is not None:
        for slug, raw_path in completed.items():
            number = _number_from_label(slug)
            numbered.setdefault(number, []).append(_resolve_record_path(raw_path, state.parent, source))
        return _validate_exact_paths(numbered, origin="state")

    if records is not None:
        for record in records:
            raw_path = next((record.get(key) for key in ("output", "path", "video", "file") if record.get(key)), None)
            if not isinstance(raw_path, str):
                raise RuntimeError(
                    f"manifest has no output path for {record['slug']!r}; provide --state or an output/path/video/file field"
                )
            numbered[int(record["number"])] = [_resolve_record_path(raw_path, manifest.parent, source)]
        return _validate_exact_paths(numbered, origin="manifest")

    excluded = tuple(path.resolve() for path in exclude)
    for path in source.rglob("*.mp4"):
        resolved = path.resolve()
        if any(resolved == root or root in resolved.parents for root in excluded):
            continue
        match = NUMBERED_NAME.match(path.name)
        if match:
            numbered.setdefault(int(match.group(1)), []).append(resolved)
    return _validate_exact_paths(numbered, origin="filename discovery")


def _stream(info: dict[str, Any], codec_type: str, path: Path) -> dict[str, Any]:
    streams = [stream for stream in info.get("streams", []) if stream.get("codec_type") == codec_type]
    if len(streams) != 1:
        raise RuntimeError(f"{path} must have exactly one {codec_type} stream, found {len(streams)}")
    stream = streams[0]
    if not stream.get("codec_name"):
        raise RuntimeError(f"{path} {codec_type} codec is unknown: {stream}")
    return stream


def _rate(value: str | None) -> float:
    if not value or value == "0/0":
        return 0.0
    try:
        return float(Fraction(value))
    except (ValueError, ZeroDivisionError) as error:
        raise RuntimeError(f"invalid frame rate {value!r}") from error


def _frame_count(video: dict[str, Any], path: Path) -> int:
    for key in ("nb_read_frames", "nb_frames"):
        value = video.get(key)
        if value not in (None, "N/A"):
            try:
                count = int(value)
            except (TypeError, ValueError):
                continue
            if count > 0:
                return count
    raise RuntimeError(f"cannot determine exact decoded frame count for {path}: {video}")


def _audio_metrics(path: Path) -> dict[str, float]:
    result = run(
        [
            "ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-vn",
            "-af", "ebur128=framelog=quiet:peak=true,silencedetect=noise=-55dB:d=1",
            "-f", "null", "-",
        ],
        capture=True,
    )
    integrated = re.findall(r"^\s*I:\s*(-?inf|-?[0-9.]+)\s+LUFS", result.stderr, re.MULTILINE)
    if not integrated or integrated[-1] == "-inf":
        raise RuntimeError(f"cannot measure finite integrated loudness for {path}")
    lufs = float(integrated[-1])
    if lufs < MIN_INTEGRATED_LUFS:
        raise RuntimeError(f"audio is effectively silent for {path}: {lufs:.1f} LUFS")
    silence_durations = [float(value) for value in re.findall(r"silence_duration:\s*([0-9.]+)", result.stderr)]
    peaks = re.findall(r"Peak:\s*(-?inf|-?[0-9.]+)\s+dBFS", result.stderr)
    return {
        "integrated_lufs": lufs,
        "true_peak_dbfs": float(peaks[-1]) if peaks and peaks[-1] != "-inf" else -999.0,
        "silence_max": max(silence_durations, default=0.0),
    }


def _decode_xerror(path: Path) -> None:
    run(
        [
            "ffmpeg", "-hide_banner", "-nostats", "-loglevel", "error", "-xerror",
            "-i", str(path), "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "-",
        ],
        capture=True,
    )


def qc(path: Path) -> dict[str, Any]:
    info = probe(path, count_frames=True)
    try:
        duration = float(info["format"]["duration"])
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError(f"duration is missing or invalid for {path}: {info}") from error
    video = _stream(info, "video", path)
    audio = _stream(info, "audio", path)
    frame_count = _frame_count(video, path)
    fps = _rate(video.get("avg_frame_rate") or video.get("r_frame_rate"))
    sample_rate = int(audio.get("sample_rate", 0))
    channels = int(audio.get("channels", 0))
    if duration < MIN_SHOT_DURATION:
        raise RuntimeError(f"shot is shorter than {MIN_SHOT_DURATION}s: {path} ({duration:.3f}s)")
    if (int(video.get("width", 0)), int(video.get("height", 0))) != (SOURCE_WIDTH, SOURCE_HEIGHT):
        raise RuntimeError(f"source video geometry must be {SOURCE_WIDTH}x{SOURCE_HEIGHT}: {path}: {video}")
    if not math.isclose(fps, MASTER_FPS, rel_tol=0.0, abs_tol=0.001):
        raise RuntimeError(f"source video must be {MASTER_FPS}fps: {path}: {fps}")
    if frame_count != SOURCE_FRAMES:
        raise RuntimeError(f"source video must contain exactly {SOURCE_FRAMES} decoded frames: {path}: {frame_count}")
    if video.get("codec_name") != "h264":
        raise RuntimeError(f"source video codec must be H.264: {path}: {video}")
    if (audio.get("codec_name"), sample_rate, channels) != ("aac", 32_000, 2):
        raise RuntimeError(f"source audio must be AAC stereo 32000Hz: {path}: {audio}")
    frame_duration = frame_count / MASTER_FPS
    if abs(duration - frame_duration) > 0.5:
        raise RuntimeError(
            f"container duration and decoded frames disagree for {path}: "
            f"duration={duration:.3f}s, frames={frame_count} ({frame_duration:.3f}s)"
        )

    _decode_xerror(path)
    freeze = run(
        [
            "ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
            "-vf", "freezedetect=n=-50dB:d=2", "-an", "-f", "null", "-",
        ],
        capture=True,
    ).stderr
    freeze_durations = [float(value) for value in re.findall(r"freeze_duration:\s*([0-9.]+)", freeze)]
    freeze_max = max(freeze_durations, default=0.0)
    if freeze_max > 8.0:
        raise RuntimeError(f"frozen-motion QC failed for {path}: {freeze_max:.2f}s")
    audio_metrics = _audio_metrics(path)
    if audio_metrics["silence_max"] >= frame_duration - 0.25:
        raise RuntimeError(f"nearly the entire audio stream is silent for {path}")
    return {
        "path": str(path),
        "duration": duration,
        "frame_count": frame_count,
        "frame_duration": frame_duration,
        "video_codec": video["codec_name"],
        "pixel_format": video.get("pix_fmt"),
        "fps": fps,
        "audio_codec": audio["codec_name"],
        "sample_rate": sample_rate,
        "channels": channels,
        "freeze_max": freeze_max,
        **audio_metrics,
    }


def expected_runtime(reports: list[dict[str, Any]]) -> float:
    return sum(int(report["frame_count"]) for report in reports) / MASTER_FPS


def build_video_filter(fit: str, frame_count: int) -> str:
    if fit == "pad":
        geometry = (
            "scale=1920:1080:force_original_aspect_ratio=decrease:"
            "force_divisible_by=2:flags=lanczos,"
            "pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black"
        )
    elif fit == "crop":
        geometry = (
            "scale=1920:1080:force_original_aspect_ratio=increase:"
            "force_divisible_by=2:flags=lanczos,crop=1920:1080"
        )
    else:
        raise ValueError(f"unknown fit policy: {fit}")
    return (
        f"{geometry},setsar=1,fps={MASTER_FPS},trim=end_frame={frame_count},"
        f"setpts=N/({MASTER_FPS}*TB),format=yuv420p"
    )


def _normalize(shot: Path, target: Path, report: dict[str, Any], fit: str) -> None:
    frame_count = int(report["frame_count"])
    duration = frame_count / MASTER_FPS
    audio_filter = (
        f"aresample={MASTER_AUDIO_RATE}:async=1:first_pts=0,apad,"
        f"atrim=duration={duration:.9f},asetpts=PTS-STARTPTS"
    )
    run(
        [
            "ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-xerror", "-i", str(shot),
            "-map", "0:v:0", "-map", "0:a:0",
            "-vf", build_video_filter(fit, frame_count), "-af", audio_filter,
            "-fps_mode", "cfr", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
            "-pix_fmt", "yuv420p", "-c:a", "pcm_s24le", "-ar", str(MASTER_AUDIO_RATE),
            "-ac", "2", "-map_metadata", "-1", str(target),
        ]
    )


def _ffconcat_quote(path: Path) -> str:
    return path.resolve().as_posix().replace("'", "'\\''")


def _validate_master(path: Path, expected_frames: int) -> dict[str, Any]:
    _decode_xerror(path)
    info = probe(path, count_frames=True)
    video = _stream(info, "video", path)
    audio = _stream(info, "audio", path)
    frames = _frame_count(video, path)
    duration = float(info["format"]["duration"])
    fps = _rate(video.get("avg_frame_rate") or video.get("r_frame_rate"))
    failures: list[str] = []
    if duration < MIN_MASTER_DURATION:
        failures.append(f"duration {duration:.3f}s < {MIN_MASTER_DURATION:.3f}s")
    if frames != expected_frames:
        failures.append(f"decoded frames {frames} != expected {expected_frames}")
    if (video.get("codec_name"), int(video.get("width", 0)), int(video.get("height", 0))) != (
        "h264", MASTER_WIDTH, MASTER_HEIGHT
    ):
        failures.append(f"video stream is not H.264 {MASTER_WIDTH}x{MASTER_HEIGHT}: {video}")
    if video.get("pix_fmt") != "yuv420p" or not math.isclose(fps, MASTER_FPS, abs_tol=0.001):
        failures.append(f"video must be yuv420p {MASTER_FPS} CFR: {video}")
    if (audio.get("codec_name"), int(audio.get("sample_rate", 0)), int(audio.get("channels", 0))) != (
        "aac", MASTER_AUDIO_RATE, 2
    ):
        failures.append(f"audio stream is not AAC stereo {MASTER_AUDIO_RATE}Hz: {audio}")
    expected_duration = expected_frames / MASTER_FPS
    if abs(duration - expected_duration) > 0.25:
        failures.append(f"duration {duration:.3f}s differs from frame runtime {expected_duration:.3f}s")
    if failures:
        raise RuntimeError(f"final master QC failed for {path}: " + "; ".join(failures))
    audio_metrics = _audio_metrics(path)
    if audio_metrics["silence_max"] >= duration - 0.25:
        raise RuntimeError(f"final master audio is nearly entirely silent: {path}")
    return {
        "probe": info,
        "frame_count": frames,
        "expected_runtime": expected_duration,
        **audio_metrics,
    }


def _write_json_atomic(path: Path, payload: Any) -> None:
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    temporary.write_text(json.dumps(payload, indent=2, allow_nan=False), encoding="utf-8")
    os.replace(temporary, path)


def master(source: Path, output: Path, work: Path, *, manifest: Path | None = None,
           state: Path | None = None, fit: str = "pad") -> None:
    source = source.resolve()
    output = output.resolve()
    work = work.resolve()
    shots = discover(source, manifest=manifest, state=state, exclude=(work, output))

    # Complete every expensive source check before creating any normalized media.
    reports = []
    for index, shot in enumerate(shots, 1):
        report = qc(shot)
        report["shot"] = index
        reports.append(report)
        print(
            "SHOT_QC", f"{index:02d}", report["frame_count"], f"{report['frame_duration']:.3f}s",
            report["video_codec"], report["audio_codec"], f"{report['sample_rate']}Hz",
            f"{report['integrated_lufs']:.1f}LUFS", flush=True,
        )
    total_frames = sum(int(report["frame_count"]) for report in reports)
    runtime = expected_runtime(reports)
    print("PREMASTER_TOTAL", SHOT_COUNT, "shots", total_frames, "frames", f"{runtime:.3f}s", flush=True)
    if runtime < MIN_MASTER_DURATION:
        raise RuntimeError(
            f"expected master runtime is shorter than {MIN_MASTER_DURATION:.0f}s: "
            f"{total_frames} frames / {MASTER_FPS}fps = {runtime:.3f}s"
        )

    work.mkdir(parents=True, exist_ok=True)
    normalized = work / "normalized"
    normalized.mkdir(exist_ok=True)
    intermediates: list[Path] = []
    for index, (shot, report) in enumerate(zip(shots, reports, strict=True), 1):
        target = normalized / f"{index:02d}.mkv"
        _normalize(shot, target, report, fit)
        normalized_probe = probe(target, count_frames=True)
        normalized_video = _stream(normalized_probe, "video", target)
        normalized_audio = _stream(normalized_probe, "audio", target)
        if _frame_count(normalized_video, target) != int(report["frame_count"]):
            raise RuntimeError(f"normalized frame count changed for shot {index:02d}: {target}")
        if normalized_audio.get("codec_name") != "pcm_s24le" or int(normalized_audio.get("sample_rate", 0)) != MASTER_AUDIO_RATE:
            raise RuntimeError(f"normalized audio is not PCM {MASTER_AUDIO_RATE}Hz: {target}: {normalized_audio}")
        intermediates.append(target)

    concat_file = work / "concat.txt"
    concat_file.write_text(
        "ffconcat version 1.0\n" + "".join(f"file '{_ffconcat_quote(path)}'\n" for path in intermediates),
        encoding="utf-8",
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary_master = output.with_name(f".{output.stem}.{uuid.uuid4().hex}.partial.mp4")
    try:
        # Video is already normalized; continuous PCM is encoded to AAC exactly once here.
        run(
            [
                "ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-xerror",
                "-f", "concat", "-safe", "0", "-i", str(concat_file),
                "-map", "0:v:0", "-map", "0:a:0", "-c:v", "copy",
                "-c:a", "aac", "-b:a", "256k", "-ar", str(MASTER_AUDIO_RATE), "-ac", "2",
                "-movflags", "+faststart", "-map_metadata", "-1", str(temporary_master),
            ]
        )
        final_report = _validate_master(temporary_master, total_frames)
        _write_json_atomic(
            work / "qc-report.json",
            {
                "selection": {
                    "shot_count": SHOT_COUNT, "total_frames": total_frames,
                    "expected_runtime": runtime, "fit_policy": fit,
                },
                "shots": reports,
                "master": final_report,
            },
        )
        os.replace(temporary_master, output)
    finally:
        if temporary_master.exists():
            temporary_master.unlink()
    print(
        "MASTER_VERIFIED", output, f"{runtime:.3f}s", f"{total_frames}frames",
        f"{MASTER_WIDTH}x{MASTER_HEIGHT}", f"{MASTER_FPS}fps", "H.264 yuv420p",
        f"AAC stereo {MASTER_AUDIO_RATE}Hz", f"fit={fit}", flush=True,
    )


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Strictly QC and master exactly 60 H3 shots into a verified 1080p feature."
    )
    parser.add_argument("--source", type=Path, required=True, help="Generation root or numbered MP4 directory")
    parser.add_argument("--output", type=Path, required=True, help="Final MP4 path; replaced only after full verification")
    parser.add_argument("--work", type=Path, required=True, help="Work directory for PCM intermediates and QC report")
    parser.add_argument("--manifest", type=Path, help="Manifest JSON; defaults to SOURCE/manifest.json when present")
    parser.add_argument("--state", type=Path, help="State JSON; defaults to SOURCE/state.json when present")
    parser.add_argument(
        "--fit", choices=("pad", "crop"), default="pad",
        help="Aspect-safe 1080p policy: pad (default, no content loss) or center crop",
    )
    args = parser.parse_args()
    master(args.source, args.output, args.work, manifest=args.manifest, state=args.state, fit=args.fit)


if __name__ == "__main__":
    main()
