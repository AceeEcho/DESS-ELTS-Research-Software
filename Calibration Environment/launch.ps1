param([switch]$SetupTracking, [switch]$StopApp, [switch]$NoBrowser, [int]$Port = 8766)
$ErrorActionPreference = 'Stop'
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$trackingSetup = Join-Path $PSScriptRoot '../scripts/setup-tracking.ps1'
if ($SetupTracking) {
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $trackingSetup
    exit $LASTEXITCODE
}
if (-not $StopApp -and -not $NoBrowser) {
    # Live trackers need a one-time setup (Python, OpenVR reader, SteamVR).
    # Manual preview works without it, so setup is offered here, never forced.
    # -Check only detects; it installs and changes nothing.
    $null = & $powershell -NoProfile -ExecutionPolicy Bypass -File $trackingSetup -Check
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Live Vive tracking is not set up on this computer yet.'
        Write-Host 'Manual preview works without it. Setup installs Python, the OpenVR reader and Steam/SteamVR.'
        $answer = Read-Host 'Set up live tracking now? [Y/N]'
        if ($answer -match '^\s*(y|yes)\s*$') {
            & $powershell -NoProfile -ExecutionPolicy Bypass -File $trackingSetup
            if ($LASTEXITCODE -ne 0) {
                Write-Host 'Tracking setup needs attention (see above). Opening the workspace in manual preview.' -ForegroundColor Yellow
            }
        }
    }
}
Set-Location -LiteralPath $PSScriptRoot
$localPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
$pythonCommand = $null
$pythonArguments = @()

# Prefer the environment dedicated to this app; otherwise use an installed
# Python 3. No absolute machine-specific path or ELTS virtualenv is required.
if (Test-Path -LiteralPath $localPython) {
    $pythonCommand = $localPython
} elseif (Get-Command py.exe -ErrorAction SilentlyContinue) {
    $pythonCommand = 'py.exe'
    $pythonArguments = @('-3')
} elseif (Get-Command python.exe -ErrorAction SilentlyContinue) {
    $pythonCommand = 'python.exe'
} else {
    Write-Host 'Python 3 is required. Run START-CALIBRATION.cmd again and answer Y to set up live tracking (it installs Python), or install Python 3 yourself.'
    exit 1
}
& $pythonCommand @pythonArguments -c 'import sys; assert sys.version_info >= (3, 10), "Python 3.10 or later is required"'
if ($LASTEXITCODE -ne 0) { exit 1 }

$serverArguments = @((Join-Path $PSScriptRoot 'server.py'), '--port', "$Port")
if ($StopApp) { $serverArguments += '--stop' }
if ($NoBrowser) { $serverArguments += '--no-browser' }
& $pythonCommand @pythonArguments @serverArguments
exit $LASTEXITCODE
