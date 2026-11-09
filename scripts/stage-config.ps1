# Stage synthetic configuration into Unity, or verify it with -Check.
[CmdletBinding()]
param([string]$PythonExecutable, [string]$Output, [string]$Report, [switch]$Check)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Invoke-EltsTool -Action 'stage' @PSBoundParameters
