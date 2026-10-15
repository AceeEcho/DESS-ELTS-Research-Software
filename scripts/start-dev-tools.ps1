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
    $process = Start-Process -FilePath $File -ArgumentList $line -WorkingDirectory $Directory -PassThru
    # Retain the handle so even an immediate crash keeps its exit code available.
    $null = $process.Handle
    return $process
}

function Wait-EltsPlayerReady {
    param($Process, [string]$LogPath, [int]$TimeoutSeconds = 120)
    # The caller supplies a unique log for this process. Never accept an older
    # player's readiness marker or confuse a running Unity window with a ready app.
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "The participant game closed before its dashboard was ready (exit code $($Process.ExitCode)). Player log: $LogPath"
        }
        $log = ''
        if (Test-Path -LiteralPath $LogPath) {
            $log = (Get-Content -LiteralPath $LogPath -Tail 100 -ErrorAction SilentlyContinue) -join "`n"
        }
        if ($log -match '(?m)^ELTS_VIEW_INIT_FAIL ([^\r\n]+)') {
            throw ("The participant view could not initialize: " + $Matches[1] +
                "`nPlayer log: $LogPath`nClose the failed participant window. Update the project to the corrected version and run START-ELTS.cmd again.")
        }
        if ($log -match '(?m)^ELTS_ADMINISTRATOR_READY http://127\.0\.0\.1:\d+/#') { return }
        # This also notices a child that exits during the wait without busy polling.
        $null = $Process.WaitForExit(250)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "The participant game did not initialize its dashboard within $TimeoutSeconds seconds. Player log: $LogPath. Check the participant window and this log. The process was left running; close it normally before retrying."
}

function Invoke-EltsDevelopmentStart {
    Set-EltsStartupStatus 'Checking this computer' 'Checking the installed tools and saved setup report.'
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
    foreach ($name in @('python','dotnet','node','unity')) {
        if (-not $paths[$name]) { $ready = $false }
    }
    Write-Host 'Starting the ELTS dashboard and participant game.'
    if (-not $ready) {
        if (Test-EltsProjectOpen $project) { throw 'Close this project in Unity, then click START-ELTS again so setup can finish.' }
        Write-Host 'Preparing this computer. First startup can take a while; complete any Unity account/license prompts.'
        Set-EltsStartupStatus 'Preparing this computer' 'Installing or checking required tools. First setup can take a while. Complete any installer or Unity sign-in/license prompts; see the console for details.'
        Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File', (Join-Path $root 'scripts/setup-dev.ps1')) -TimeoutSeconds 14400 `
            -Activity 'Prepare this computer' -ReportPaths $reportPath -RetryCommand 'START-ELTS.cmd'
        Initialize-EltsLocalTools
        $paths = Get-EltsSetupPaths $manifest (Join-Path $env:LOCALAPPDATA 'ELTS/Tools') $env:ELTS_PYTHON $env:ELTS_UNITY_EDITOR
    }
    Set-EltsStartupStatus 'Checking application files' 'Comparing source and configuration files with the saved build.'
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
                    Set-EltsStartupStatus 'Verifying the saved app' 'Checking the participant game and its required data files before opening it.'
                    Invoke-EltsSetupProcess $paths.python @('-X','utf8','-m',
                        'tools.build.verify', (Split-Path -Parent $candidate))
                    $player = $candidate
                }
            }
        } catch {
            Write-Warning ("The saved build could not be verified; ELTS will build a fresh copy. " + $_.Exception.Message)
            $player = $null
        }
    }
    if (-not $player) {
        if (Test-EltsProjectOpen $project) { throw 'The dashboard needs a build. Close this project in Unity, then click START-ELTS again.' }
        # Build to a new directory; never delete an older build or its recordings.
        $output = 'build/start-elts-' + [Guid]::NewGuid().ToString('N')
        Write-Host 'Building the dashboard and participant game...'
        Set-EltsStartupStatus 'Building ELTS' 'Unity is building the dashboard and participant game. This can take several minutes. The console shows build output; leave startup running.'
        Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File',
            (Join-Path $root 'scripts/build.ps1'), '-Output', $output,
            '-PythonExecutable', $paths.python, '-UnityEditor', $paths.unity) -TimeoutSeconds 3600 `
            -Activity 'Build dashboard and participant game' -ReportPaths (Join-Path $root 'diagnostics/build-report.json') -RetryCommand 'START-ELTS.cmd'
        $player = Join-Path $root ($output + '/ELTS-Synthetic.exe')
        if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw 'Build finished without a dashboard executable.' }
        # Staging during the build can change generated Assets, so hash afterwards.
        Set-EltsStartupStatus 'Saving build information' 'Recording the source fingerprint so later launches can reuse this build.'
        $cache = @{ fingerprint = Get-EltsStartupFingerprint; output = $output }
        [IO.File]::WriteAllText($cachePath, ($cache | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    }
    # Unity runs headlessly only when setup/build requires it. Open the Editor
    # manually for source work; ordinary testing needs just the two app windows.
    # Preserve the connected desktop at startup; fullscreen remains a user action.
    Set-EltsStartupStatus 'Opening ELTS' 'Starting the participant game. Its administrator dashboard will open in your browser.'
    # Keep each attempt's Unity diagnostics, including failed scene initialization.
    $playerLog = Join-Path $root ('diagnostics/player-start-' + [Guid]::NewGuid().ToString('N') + '.log')
    Write-Host "Player log: $playerLog"
    $process = Start-EltsVisibleApp $player @('-eltsWindowed','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',$playerLog) (Split-Path -Parent $player)
    try {
        Set-EltsStartupStatus 'Initializing the participant view' 'Waiting for the game scene and administrator server. An initialization error will appear here; first launches may take longer.'
        Wait-EltsPlayerReady -Process $process -LogPath $playerLog
    } finally { $process.Dispose() }
    Write-Host 'Participant view and administrator server initialized.' -ForegroundColor Green
}
