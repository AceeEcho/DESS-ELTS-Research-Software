[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor, [string]$Report)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'start-study' @PSBoundParameters
