[CmdletBinding()]
param(
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

$hubPath = 'C:\Program Files\Unity Hub\Unity Hub.exe'
if (-not (Test-Path -LiteralPath $hubPath)) {
    throw "Unity Hub was not found at '$hubPath'."
}

# Hub 3.18 calls path.join() with ALLUSERSPROFILE during bootstrap. Some
# automation shells omit this standard Windows variable, which makes Hub exit
# before its normal log is created. Set it only for this process tree.
if ([string]::IsNullOrWhiteSpace($env:ALLUSERSPROFILE)) {
    $programDataPath = if ([string]::IsNullOrWhiteSpace($env:PROGRAMDATA)) {
        'C:\ProgramData'
    } else {
        $env:PROGRAMDATA
    }

    if (-not (Test-Path -LiteralPath $programDataPath)) {
        throw "The shared Windows data directory '$programDataPath' does not exist."
    }

    $env:ALLUSERSPROFILE = $programDataPath
}

$hub = Start-Process -FilePath $hubPath -PassThru
Write-Host "Unity Hub started (PID $($hub.Id))."
Write-Host 'Sign in, then refresh or activate Unity Personal before running Build-Windows.ps1.'

if ($Wait) {
    $hub.WaitForExit()
    exit $hub.ExitCode
}
