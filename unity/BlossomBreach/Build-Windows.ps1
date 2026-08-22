[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.9f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor was not found at '$UnityPath'."
}

$projectPath = $PSScriptRoot
$logDirectory = Join-Path $projectPath 'Builds\Logs'
$logPath = Join-Path $logDirectory 'windows-build.log'
New-Item -ItemType Directory -Force $logDirectory | Out-Null

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $UnityPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true

# Unity's Package Manager also reads ALLUSERSPROFILE. Codex/automation shells
# can omit it even though the normal desktop environment supplies it, causing
# package resolution to fail before compilation. Set it only on this child.
$programDataPath = if ([string]::IsNullOrWhiteSpace($env:PROGRAMDATA)) {
    'C:\ProgramData'
} else {
    $env:PROGRAMDATA
}
if (-not (Test-Path -LiteralPath $programDataPath)) {
    throw "The shared Windows data directory '$programDataPath' does not exist."
}
$startInfo.Environment['ALLUSERSPROFILE'] = $programDataPath

$escapedProjectPath = $projectPath.Replace('"', '\"')
$escapedLogPath = $logPath.Replace('"', '\"')
$startInfo.Arguments = "-batchmode -nographics -quit -projectPath `"$escapedProjectPath`" " +
    "-executeMethod BlossomBreach.BuildBlossom.BuildWindows -logFile `"$escapedLogPath`""

Write-Host "Building Blossom Breach with $UnityPath"
$process = [System.Diagnostics.Process]::Start($startInfo)
$process.WaitForExit()

if ($process.ExitCode -ne 0) {
    Write-Warning "Unity exited with code $($process.ExitCode). Last build log lines follow."
    if (Test-Path -LiteralPath $logPath) {
        Get-Content -Tail 80 -LiteralPath $logPath
    }
    exit $process.ExitCode
}

$executable = Join-Path $projectPath 'Builds\Windows\BlossomBreach.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Unity returned success, but '$executable' was not created. See '$logPath'."
}

Write-Host "Build complete: $executable"
Write-Host "Log: $logPath"
