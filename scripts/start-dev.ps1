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
    try { $lock = [IO.File]::Open((Join-Path $diagnostics 'start.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        throw 'ELTS could not acquire the startup lock. Another START-ELTS window may be preparing or building the app. Let that window finish and read its progress/errors before retrying. Do not delete diagnostics/start.lock while startup is running.'
    }
    Start-Transcript -Path (Join-Path $diagnostics 'start-console.log') -Force | Out-Null
    $transcribing = $true
    Invoke-EltsDevelopmentStart
} catch {
    Write-Host ('START STOPPED: ' + $_.Exception.Message) -ForegroundColor Red
    if ($transcribing) { Write-Host "Startup transcript: $(Join-Path $diagnostics 'start-console.log')" }
    Write-Host "Troubleshooting: $(Join-Path $script:EltsRepositoryRoot 'docs/operator/multi-machine-setup.md')"
    $exitCode = 1
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    if ($lock) { $lock.Dispose() }
}
exit $exitCode
