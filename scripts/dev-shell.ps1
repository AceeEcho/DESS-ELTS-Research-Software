# Start an ordinary PowerShell session with this clone's detected tool paths.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location -LiteralPath $script:EltsRepositoryRoot
Write-Host 'ELTS development shell. Tool paths apply to this window only.'
Write-Host 'Use git status before committing. Type exit to close this shell.'
