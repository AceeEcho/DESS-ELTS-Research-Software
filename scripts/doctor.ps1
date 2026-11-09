# Report development readiness only, never physical acceptance.
[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor, [string]$Report)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'doctor' @PSBoundParameters
