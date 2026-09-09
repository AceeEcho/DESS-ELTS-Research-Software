# Shared path/prerequisite handling. No machine-specific paths are committed.
$script:EltsRepositoryRoot = Split-Path -Parent $PSScriptRoot

function Initialize-EltsLocalTools {
    # Never dot-source machine configuration. It is data, and missing paths from
    # another computer are ignored so setup can rediscover local installations.
    $localTools = Join-Path $script:EltsRepositoryRoot 'config/local/development-tools.json'
    if (-not (Test-Path -LiteralPath $localTools -PathType Leaf)) { return }
    try { $paths = Get-Content -LiteralPath $localTools -Raw | ConvertFrom-Json }
    catch {
        Write-Warning 'Local development tool paths could not be read. Run SETUP-DEV.cmd to rediscover them.'
        return
    }
    foreach ($name in @('git','python','dotnet','node','unityCli')) {
        $property = $paths.PSObject.Properties[$name]
        if ($property -and $property.Value -and (Test-Path -LiteralPath $property.Value -PathType Leaf)) {
            $directory = Split-Path -Parent $property.Value
            $env:PATH = $directory + [IO.Path]::PathSeparator + $env:PATH
            if ($name -eq 'python' -and -not $env:ELTS_PYTHON) { $env:ELTS_PYTHON = $property.Value }
            if ($name -eq 'dotnet') { $env:DOTNET_ROOT = $directory }
        }
    }
    $editor = $paths.PSObject.Properties['unity']
    if ($editor -and $editor.Value -and -not $env:ELTS_UNITY_EDITOR -and (Test-Path -LiteralPath $editor.Value -PathType Leaf)) {
        $env:ELTS_UNITY_EDITOR = $editor.Value
    }
}

Initialize-EltsLocalTools

function Resolve-EltsPython {
    param([string]$PythonExecutable)
    $candidates = @()
    if ($PythonExecutable) { $candidates += $PythonExecutable }
    elseif ($env:ELTS_PYTHON) { $candidates += $env:ELTS_PYTHON }
    else {
        $command = Get-Command python -ErrorAction SilentlyContinue
        if ($command -and $command.Source -notlike '*WindowsApps*') { $candidates += $command.Source }
        $launcher = Get-Command py -ErrorAction SilentlyContinue
        if ($launcher) {
            $resolved = & $launcher.Source -3 -c 'import sys; print(sys.executable)' 2>$null
            if ($LASTEXITCODE -eq 0) { $candidates += $resolved }
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'Provide a supported Python executable with -PythonExecutable or ELTS_PYTHON; see config/toolchain.json.'
}

function Invoke-EltsTool {
    param([string]$Action, [string]$PythonExecutable, [string]$UnityEditor,
          [string]$Suite = 'all', [string]$Output, [string]$Report, [switch]$Check)
    $python = Resolve-EltsPython -PythonExecutable $PythonExecutable
    $arguments = @('-X', 'utf8', (Join-Path $script:EltsRepositoryRoot 'tools/bootstrap/driver.py'), '--action', $Action)
    if ($UnityEditor) { $arguments += @('--unity-editor', $UnityEditor) }
    if ($Suite) { $arguments += @('--suite', $Suite) }
    if ($Output) { $arguments += @('--output', $Output) }
    if ($Report) { $arguments += @('--report', $Report) }
    if ($Check) { $arguments += '--check' }
    & $python @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
