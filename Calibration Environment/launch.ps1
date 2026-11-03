param([switch]$SetupTracking, [switch]$StopApp, [switch]$NoBrowser, [int]$Port = 8766)
$ErrorActionPreference = 'Stop'
if ($SetupTracking) {
    & (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot '../scripts/setup-tracking.ps1')
    exit $LASTEXITCODE
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
    Write-Host 'Python 3 is required. Run the project SETUP-DEV.cmd or install Python 3, then try again.'
    exit 1
}
& $pythonCommand @pythonArguments -c 'import sys; assert sys.version_info >= (3, 10), "Python 3.10 or later is required"'
if ($LASTEXITCODE -ne 0) { exit 1 }

$serverArguments = @((Join-Path $PSScriptRoot 'server.py'), '--port', "$Port")
if ($StopApp) { $serverArguments += '--stop' }
if ($NoBrowser) { $serverArguments += '--no-browser' }
& $pythonCommand @pythonArguments @serverArguments
exit $LASTEXITCODE
