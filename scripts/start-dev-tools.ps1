# Startup decisions are separated from process execution for regression testing.
function Test-EltsProjectOpen {
    param([string]$Project)
    $lockPath = Join-Path $Project 'Temp/UnityLockfile'
    if (-not (Test-Path -LiteralPath $lockPath)) { return $false }
    try {
        $handle = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None')
        $handle.Dispose()
        return $false
    } catch { return $true }
}

function Get-EltsStartupFingerprint {
    # Bind the cached build to source/configuration content, including local edits.
    # Unity caches, recordings and other generated outputs are deliberately omitted.
    $entries = foreach ($relative in @('unity/Assets','unity/Packages','unity/ProjectSettings',
            'config','tools/build','tools/bootstrap','scripts')) {
        Get-ChildItem -LiteralPath (Join-Path $script:EltsRepositoryRoot $relative) -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](__pycache__|local)[\\/]' } |
            ForEach-Object {
                $_.FullName.Substring($script:EltsRepositoryRoot.Length) + ':' +
                    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(
            (($entries | Sort-Object) -join "`n")))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Start-EltsVisibleApp {
    param([string]$File, [string[]]$Arguments, [string]$Directory)
    $line = ($Arguments | ForEach-Object { ConvertTo-EltsProcessArgument $_ }) -join ' '
    # These are the interactive windows the user explicitly requested.
    Start-Process -FilePath $File -ArgumentList $line -WorkingDirectory $Directory | Out-Null
}

function Invoke-EltsDevelopmentStart {
    $root = $script:EltsRepositoryRoot
    Set-Location -LiteralPath $root
    $manifestPath = Join-Path $root 'config/toolchain.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-EltsSetupManifest $manifest
    $project = Join-Path $root $manifest.unity.projectPath
    $shell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $paths = Get-EltsSetupPaths $manifest (Join-Path $env:LOCALAPPDATA 'ELTS/Tools') $env:ELTS_PYTHON $env:ELTS_UNITY_EDITOR
    $ready = $false
    $reportPath = Join-Path $root 'diagnostics/setup-report.json'
    if (Test-Path -LiteralPath $reportPath) {
        try {
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $ready = $report.result -eq 'ready' -and $report.toolchainSha256 -eq (Get-FileHash $manifestPath -Algorithm SHA256).Hash
        } catch { $ready = $false }
    }
    foreach ($name in @('git','python','dotnet','node','unity')) {
        if (-not $paths[$name]) { $ready = $false }
    }
    Write-Host 'Starting the ELTS dashboard and participant game.'
    if (-not $ready) {
        if (Test-EltsProjectOpen $project) { throw 'Close this project in Unity, then click START-ELTS again so setup can finish.' }
        Write-Host 'Preparing this computer. First startup can take a while; complete any Unity account/license prompts.'
        Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File', (Join-Path $root 'scripts/setup-dev.ps1')) -TimeoutSeconds 14400
        Initialize-EltsLocalTools
        $paths = Get-EltsSetupPaths $manifest (Join-Path $env:LOCALAPPDATA 'ELTS/Tools') $env:ELTS_PYTHON $env:ELTS_UNITY_EDITOR
    }
    $fingerprint = Get-EltsStartupFingerprint
    $cachePath = Join-Path $root 'diagnostics/start-build.json'
    $player = $null
    if (Test-Path -LiteralPath $cachePath) {
        try {
            $cache = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
            # Accept only our own relative generated build directories.
            if ($cache.fingerprint -eq $fingerprint -and $cache.output -match '^build/start-elts-[a-f0-9]{32}$') {
                $candidate = Join-Path $root ($cache.output + '/ELTS-Synthetic.exe')
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    # An executable alone is insufficient: verify DLLs and data too.
                    Invoke-EltsSetupProcess $paths.python @('-X','utf8','-m',
                        'tools.build.verify', (Split-Path -Parent $candidate))
                    $player = $candidate
                }
            }
        } catch { $player = $null }
    }
    if (-not $player) {
        if (Test-EltsProjectOpen $project) { throw 'The dashboard needs a build. Close this project in Unity, then click START-ELTS again.' }
        # Build to a new directory; never delete an older build or its recordings.
        $output = 'build/start-elts-' + [Guid]::NewGuid().ToString('N')
        Write-Host 'Building the dashboard and participant game...'
        Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File',
            (Join-Path $root 'scripts/build.ps1'), '-Output', $output,
            '-PythonExecutable', $paths.python, '-UnityEditor', $paths.unity) -TimeoutSeconds 3600
        $player = Join-Path $root ($output + '/ELTS-Synthetic.exe')
        if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw 'Build finished without a dashboard executable.' }
        # Staging during the build can change generated Assets, so hash afterwards.
        $cache = @{ fingerprint = Get-EltsStartupFingerprint; output = $output }
        [IO.File]::WriteAllText($cachePath, ($cache | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    }
    # Unity runs headlessly only when setup/build requires it. Open the Editor
    # manually for source work; ordinary testing needs just the two app windows.
    # Preserve the connected desktop at startup; fullscreen remains a user action.
    Start-EltsVisibleApp $player @('-eltsWindowed','-screen-fullscreen','0','-screen-width','1280','-screen-height','720') (Split-Path -Parent $player)
    Write-Host 'Dashboard and participant game launch requested.' -ForegroundColor Green
}
