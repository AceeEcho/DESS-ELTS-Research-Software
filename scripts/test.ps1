[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor,
      [ValidateSet('all','baseline','validation','config','geometry','runtime','unity-edit','unity-play')]
      [string]$Suite = 'all', [string]$Output, [string]$Report, [switch]$Check)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'test' @PSBoundParameters
