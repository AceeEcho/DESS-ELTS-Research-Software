# Create local configuration if needed, stage it and run the development checks.
[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor, [string]$Report)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'bootstrap' @PSBoundParameters
