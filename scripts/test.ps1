# Run one test suite, or all of them.
[CmdletBinding()]
param([string]$PythonExecutable, [string]$UnityEditor,
      [ValidateSet('all','baseline','validation','config','geometry','runtime','unity-edit','unity-play')]
      [string]$Suite = 'all', [string]$Report)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'test' @PSBoundParameters
