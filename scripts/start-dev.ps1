# One-click development startup. Study startup remains separately fail-closed.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'setup-tools.ps1')
. (Join-Path $PSScriptRoot 'start-dev-tools.ps1')
$lock = $null
$transcribing = $false
$exitCode = 0
try {
    $diagnostics = Join-Path $script:EltsRepositoryRoot 'diagnostics'
    New-Item -ItemType Directory -Force $diagnostics | Out-Null
    # A second click must not start a competing installer or build.
    $lock = [IO.File]::Open((Join-Path $diagnostics 'start.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    Start-Transcript -Path (Join-Path $diagnostics 'start-console.log') -Force | Out-Null
    $transcribing = $true
    Invoke-EltsDevelopmentStart
} catch {
    Write-Host ('START STOPPED: ' + $_.Exception.Message) -ForegroundColor Red
    $exitCode = 1
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    if ($lock) { $lock.Dispose() }
}
exit $exitCode
