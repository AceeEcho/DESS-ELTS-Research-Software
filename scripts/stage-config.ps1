[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor,
      [ValidateSet('all','baseline','plan','progress','config','geometry','runtime','unity-edit','unity-play')]
      [string]$Suite = 'all', [string]$Output, [string]$Report, [switch]$Check)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'stage' @PSBoundParameters
