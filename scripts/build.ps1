# Build the synthetic Windows player into -Output (default build/windows-synthetic).
[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor, [string]$Output, [string]$Report,
      [switch]$UseCalibrationProfile)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'build' @PSBoundParameters
