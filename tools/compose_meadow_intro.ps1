param(
  [string]$Ffmpeg = "C:\Users\2610\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0-full_build\bin\ffmpeg.exe",
  [string]$Ffprobe = "C:\Users\2610\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0-full_build\bin\ffprobe.exe",
  [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$images = 1..3 | ForEach-Object { Join-Path $Root ("game\assets\images\meadow-intro-0$_.png") }
$output = Join-Path $Root "game\assets\video\meadow-animatic-preview.mp4"
$outputDirectory = Split-Path -Parent $output
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("blossom-intro-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

try {
  $clips = @()
  for ($index = 0; $index -lt $images.Count; $index++) {
    $clip = Join-Path $tempDirectory ("shot-{0:D2}.mp4" -f ($index + 1))
    $direction = if ($index -eq 1) { "iw/2-(iw/zoom/2)-on*0.55" } else { "iw/2-(iw/zoom/2)+on*0.42" }
    $filter = "scale=1600:900:force_original_aspect_ratio=increase,crop=1600:900," +
      "zoompan=z='min(zoom+0.0018,1.13)':x='$direction':y='ih/2-(ih/zoom/2)':d=72:s=1344x768:fps=24," +
      "eq=saturation=1.06:contrast=1.03,fade=t=in:st=0:d=0.12,fade=t=out:st=2.82:d=0.18,format=yuv420p"
    & $Ffmpeg -hide_banner -loglevel error -y -loop 1 -framerate 24 -i $images[$index] -t 3 -vf $filter -an -c:v libx264 -preset medium -crf 17 -movflags +faststart $clip
    if ($LASTEXITCODE -ne 0) { throw "Shot encode failed: $($images[$index])" }
    $clips += $clip
  }

  $concatList = Join-Path $tempDirectory "clips.txt"
  $concatLines = $clips | ForEach-Object { "file '$($_ -replace "'", "''")'" }
  [System.IO.File]::WriteAllLines($concatList, $concatLines, [System.Text.UTF8Encoding]::new($false))
  $videoOnly = Join-Path $tempDirectory "video.mp4"
  & $Ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $concatList -c copy $videoOnly
  if ($LASTEXITCODE -ne 0) { throw "Video concatenation failed" }

  $staged = Join-Path $outputDirectory (".meadow-animatic-preview-" + [guid]::NewGuid() + ".mp4")
  $audioFilter = "[1:a]volume=0.035,lowpass=f=850[amb];" +
    "[2:a]volume=0.16,afade=t=out:st=0.18:d=0.32,adelay=650|650[hit1];" +
    "[3:a]volume=0.12,afade=t=out:st=0.20:d=0.35,adelay=3380|3380[hit2];" +
    "[4:a]volume=0.18,afade=t=out:st=0.22:d=0.40,adelay=6950|6950[hit3];" +
    "[amb][hit1][hit2][hit3]amix=inputs=4:normalize=0,alimiter=limit=0.9[a]"
  & $Ffmpeg -hide_banner -loglevel error -y -i $videoOnly `
    -f lavfi -i "anoisesrc=color=pink:duration=9:sample_rate=48000" `
    -f lavfi -i "sine=frequency=190:duration=0.5:sample_rate=48000" `
    -f lavfi -i "sine=frequency=330:duration=0.55:sample_rate=48000" `
    -f lavfi -i "sine=frequency=720:duration=0.65:sample_rate=48000" `
    -filter_complex $audioFilter -map 0:v -map "[a]" -c:v copy -c:a aac -b:a 160k -shortest -movflags +faststart $staged
  if ($LASTEXITCODE -ne 0) { throw "Audio mix failed" }

  $probe = & $Ffprobe -v error -show_entries stream=codec_name,width,height,r_frame_rate -show_entries format=duration -of json $staged
  if ($LASTEXITCODE -ne 0 -or -not ($probe | Select-String '"width": 1344')) { throw "Output verification failed" }
  Move-Item -Force -LiteralPath $staged -Destination $output
  Write-Output $probe
  Write-Output "WROTE $output"
}
finally {
  if (Test-Path -LiteralPath $tempDirectory) { Remove-Item -Recurse -Force -LiteralPath $tempDirectory }
}
