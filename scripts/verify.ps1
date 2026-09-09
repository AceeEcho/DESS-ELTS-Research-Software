[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
# One entry point for current repository/runtime checks. Each action retains
# its own diagnostic report and stops with a nonzero exit on failure.
Invoke-EltsTool -Action 'doctor' @PSBoundParameters
Invoke-EltsTool -Action 'test' -Suite 'all' @PSBoundParameters
