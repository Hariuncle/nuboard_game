#!/usr/bin/env bash

set -Eeuo pipefail

usage() {
  printf 'Usage: %s SHOT1.mp4 SHOT2.mp4 SHOT3.mp4 OUTPUT.mp4\n' "${0##*/}" >&2
}

die() {
  printf 'compose_h3_story: %s\n' "$*" >&2
  exit 1
}

[[ $# -eq 4 ]] || {
  usage
  exit 64
}

for command_name in ffmpeg ffprobe fc-match mktemp cp cmp mv; do
  command -v "$command_name" >/dev/null 2>&1 || die "required command not found: $command_name"
done

input_paths=("$1" "$2" "$3")
output_path=$4

for input_path in "${input_paths[@]}"; do
  [[ -f "$input_path" ]] || die "input is not a file: $input_path"
  [[ -s "$input_path" ]] || die "input is empty: $input_path"
  [[ "$(ffprobe -v error -select_streams v:0 -show_entries stream=codec_type -of default=nw=1:nk=1 -- "$input_path")" == "video" ]] \
    || die "input has no readable video stream: $input_path"
done

output_dir=$(dirname -- "$output_path")
output_name=$(basename -- "$output_path")
mkdir -p -- "$output_dir"
output_dir=$(cd -- "$output_dir" && pwd -P)
output_path="$output_dir/$output_name"
[[ ! -d "$output_path" ]] || die "output path is a directory: $output_path"

temp_dir=$(mktemp -d "${TMPDIR:-/tmp}/compose-h3-story.XXXXXXXX")
staged_output=""
cleanup() {
  [[ -z "$staged_output" ]] || rm -f -- "$staged_output" || true
  rm -rf -- "$temp_dir" || true
}
trap cleanup EXIT HUP INT TERM

font_supports_korean() {
  local pattern=$1
  local languages

  # `%{lang}` is the language set advertised by the matched font itself, not
  # merely the requested pattern. Require the exact `ko` token so an arbitrary
  # Latin fallback (for example, a Hangul-less DejaVu installation) is rejected.
  languages=$(fc-match -f '%{lang}\n' "$pattern" | head -n 1)
  languages=${languages//,/|}
  languages=${languages//;/|}
  languages=${languages// /|}
  [[ "|$languages|" == *"|ko|"* ]]
}

detect_korean_font() {
  local candidate matched pattern
  local candidates=(
    "Noto Sans CJK KR"
    "Noto Sans KR"
    "NanumGothic"
    "Nanum Gothic"
    "DejaVu Sans"
  )

  for candidate in "${candidates[@]}"; do
    pattern="${candidate}:lang=ko"
    matched=$(fc-match -f '%{family[0]}\n' "$pattern" | head -n 1)
    if [[ "$matched" == *"$candidate"* ]] && font_supports_korean "$pattern"; then
      printf '%s\n' "$matched"
      return 0
    fi
  done

  # Permit a differently named fallback only when its own Fontconfig metadata
  # explicitly declares Korean coverage.
  pattern=':lang=ko'
  matched=$(fc-match -f '%{family[0]}\n' "$pattern" | head -n 1)
  [[ -n "$matched" ]] && font_supports_korean "$pattern" || return 1
  printf '%s\n' "$matched"
}

font_family=$(detect_korean_font) || die "no Korean-capable font was found by fontconfig"
# ASS uses commas as field separators; family aliases do not need to be preserved.
font_family=${font_family//,/ }
printf 'Using caption font: %s\n' "$font_family"

normalize_shot() {
  local source=$1
  local destination=$2

  # Every act is exactly 56 frames (2.333 s). Short inputs hold their last frame;
  # long inputs are trimmed. This guarantees a stable 168-frame final sequence.
  ffmpeg -hide_banner -loglevel warning -y \
    -i "$source" \
    -map 0:v:0 -an \
    -vf "scale=1344:768:force_original_aspect_ratio=decrease:flags=lanczos,pad=1344:768:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps=24,tpad=stop_mode=clone:stop_duration=3,trim=end_frame=56,setpts=N/(24*TB),format=yuv420p" \
    -frames:v 56 \
    -c:v libx264 -preset slow -crf 16 -profile:v high -level:v 4.1 \
    -g 48 -keyint_min 48 -sc_threshold 0 \
    -video_track_timescale 24000 -movflags +faststart \
    "$destination"
}

for index in 0 1 2; do
  normalize_shot "${input_paths[$index]}" "$temp_dir/shot$((index + 1)).mp4"
done

cat >"$temp_dir/concat.txt" <<'CONCAT_EOF'
file 'shot1.mp4'
file 'shot2.mp4'
file 'shot3.mp4'
CONCAT_EOF

(
  cd -- "$temp_dir"
  ffmpeg -hide_banner -loglevel warning -y \
    -f concat -safe 1 -i concat.txt \
    -map 0:v:0 -an -c copy combined.mp4
)

cat >"$temp_dir/captions.ass" <<ASS_EOF
[Script Info]
Title: NEON BREACH H3 story intro
ScriptType: v4.00+
WrapStyle: 0
ScaledBorderAndShadow: yes
YCbCr Matrix: TV.709
PlayResX: 1344
PlayResY: 768

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Caption,$font_family,46,&H00FFFFFF,&H000000FF,&H00200D04,&H90000000,-1,0,0,0,100,100,0.4,0,3,2,0,2,44,44,42,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:00.15,0:00:02.15,Caption,,0,0,0,,훈련망 침입 감지
Dialogue: 0,0:00:02.48,0:00:04.55,Caption,,0,0,0,,오버시어가 훈련망을 장악했다
Dialogue: 0,0:00:04.85,0:00:06.85,Caption,,0,0,0,,조준 링크 연결 — BREACH
ASS_EOF

(
  cd -- "$temp_dir"
  ffmpeg -hide_banner -loglevel warning -y \
    -i combined.mp4 \
    -map 0:v:0 -an \
    -vf "subtitles=captions.ass,format=yuv420p" \
    -frames:v 168 -r 24 \
    -c:v libx264 -preset slow -crf 16 -profile:v high -level:v 4.1 \
    -g 48 -keyint_min 48 -sc_threshold 0 \
    -video_track_timescale 24000 -movflags +faststart \
    final.mp4
)

codec=$(ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of default=nw=1:nk=1 -- "$temp_dir/final.mp4")
dimensions=$(ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0:s=x -- "$temp_dir/final.mp4")
frame_rate=$(ffprobe -v error -select_streams v:0 -show_entries stream=avg_frame_rate -of default=nw=1:nk=1 -- "$temp_dir/final.mp4")
duration=$(ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 -- "$temp_dir/final.mp4")
audio_streams=$(ffprobe -v error -select_streams a -show_entries stream=index -of csv=p=0 -- "$temp_dir/final.mp4" | wc -l | tr -d '[:space:]')

[[ "$codec" == "h264" ]] || die "verification failed: codec is $codec, expected h264"
[[ "$dimensions" == "1344x768" ]] || die "verification failed: dimensions are $dimensions, expected 1344x768"
awk -v rate="$frame_rate" 'BEGIN { split(rate, n, "/"); exit !(n[2] != 0 && n[1] / n[2] > 23.99 && n[1] / n[2] < 24.01) }' \
  || die "verification failed: frame rate is $frame_rate, expected 24 fps"
awk -v seconds="$duration" 'BEGIN { exit !(seconds >= 6.90 && seconds <= 7.10) }' \
  || die "verification failed: duration is $duration seconds, expected about 7 seconds"
[[ "$audio_streams" == "0" ]] || die "verification failed: final video unexpectedly contains audio"

# Stage a complete byte-identical copy beside the destination. The last move is
# therefore a same-filesystem atomic rename: a failed copy/sync/rename leaves any
# existing destination untouched, and the EXIT trap removes the staged file.
staged_output=$(mktemp "$output_dir/.compose-h3-story.XXXXXXXX")
cp -f -- "$temp_dir/final.mp4" "$staged_output"
cmp -s -- "$temp_dir/final.mp4" "$staged_output" \
  || die "placement verification failed: staged output differs from encoded video"
[[ "$(ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of default=nw=1:nk=1 -- "$staged_output")" == "h264" ]] \
  || die "placement verification failed: staged output is not readable H.264"

if command -v sync >/dev/null 2>&1; then
  if ! sync -f -- "$staged_output" 2>/dev/null; then
    sync "$staged_output" 2>/dev/null \
      || printf 'Warning: could not explicitly sync staged output; continuing after full byte comparison.\n' >&2
  fi
fi

mv -fT -- "$staged_output" "$output_path"
staged_output=""

printf 'Created %s\n' "$output_path"
printf 'Verified: codec=%s, size=%s, fps=%s, duration=%ss, audio=none\n' \
  "$codec" "$dimensions" "$frame_rate" "$duration"
