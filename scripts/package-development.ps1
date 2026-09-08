[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string[]]$Evidence,
    [string]$SampleRun,
    [string]$PythonExecutable
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$python = Resolve-EltsPython -PythonExecutable $PythonExecutable
$arguments = @('-X', 'utf8', (Join-Path $script:EltsRepositoryRoot 'tools/build/package.py'), '--build', $BuildDirectory, '--output', $Output)
foreach ($report in $Evidence) { $arguments += @('--evidence', $report) }
if ($SampleRun) { $arguments += @('--sample-run', $SampleRun) }
& $python @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
