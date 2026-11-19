# Startup decisions are separated from process execution for regression testing.
$script:EltsFingerprintGroups = @{}

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

function Get-EltsCalibrationRoomEntry {
    param([string]$Profiles)
    # The room file owns the shared screen settings. Older installations use the
    # latest profile until their first save in the updated calibration workspace.
    $roomFile = Join-Path (Split-Path -Parent $Profiles) 'station-room.json'
    $latest = if (Test-Path -LiteralPath $roomFile -PathType Leaf) {
        Get-Item -LiteralPath $roomFile
    } elseif (Test-Path -LiteralPath $Profiles -PathType Container) {
        Get-ChildItem -LiteralPath $Profiles -File | Where-Object {
            $_.Name -cmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}\.json$'
        } | Sort-Object @{ Expression = { $_.LastWriteTimeUtc.Ticks } }, Name | Select-Object -Last 1
    }
    if (-not $latest) { return @() }
    try {
        $saved = Get-Content -LiteralPath $latest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $calibration = if ($null -ne $saved.format) { $saved.calibration } else { $saved }
        $room = [ordered]@{ screen = $calibration.screen; weaponSerial = [string]$calibration.weapon.serial }
        return @('calibration-room:' + ($room | ConvertTo-Json -Compress -Depth 20))
    } catch {
        # Unreadable files still change the fingerprint; staging reports the error.
        return @('calibration-file:' + $latest.Name + ':' +
            (Get-FileHash -LiteralPath $latest.FullName -Algorithm SHA256).Hash)
    }
}

function Get-EltsStartupFingerprint {
    # Bind the cached build to source/configuration content, including local edits.
    # Unity caches, recordings and other generated outputs are deliberately omitted.
    # Compare repository-relative paths. An installation under AppData\Local must
    # still hash its source files. Cache only group digests for diagnosis.
    $groups = [ordered]@{}
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in @('unity/Assets','unity/Packages','unity/ProjectSettings',
                'config','schemas','tools/config','tools/build','tools/bootstrap','scripts')) {
            $files = foreach ($path in [IO.Directory]::EnumerateFiles((Join-Path $script:EltsRepositoryRoot $relative), '*', 'AllDirectories')) {
                $name = $path.Substring($script:EltsRepositoryRoot.Length).Replace('\','/')
                if ($name -match '/__pycache__/|^/config/local/|^/unity/Assets/StreamingAssets/config-generated(?:/|\.meta$)') { continue }
                $stream = [IO.File]::OpenRead($path)
                try { $name + ':' + [BitConverter]::ToString($sha.ComputeHash($stream)) }
                finally { $stream.Dispose() }
            }
            $sorted = ($files | Sort-Object) -join "`n"
            $groups[$relative] = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sorted)))
        }
        $stationRoot = if ($env:ELTS_STATION_HOME) { $env:ELTS_STATION_HOME } else { $script:EltsRepositoryRoot }
        $room = (Get-EltsCalibrationRoomEntry (Join-Path $stationRoot 'Calibration Environment/profiles')) -join "`n"
        $groups['room configuration'] = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($room)))
        $script:EltsFingerprintGroups = $groups
        $all = $groups.Values -join "`n"
        return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($all))).Replace('-', '').ToLowerInvariant()
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
        if ($log -match '(?m)^ELTS_ADMINISTRATOR_READY(?:\s|$)') { return }
        # This also notices a child that exits during the wait without busy polling.
        $null = $Process.WaitForExit(250)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "The participant game did not initialize its dashboard within $TimeoutSeconds seconds. Player log: $LogPath. Check the participant window and this log. The process was left running; close it normally before retrying."
}

function Invoke-EltsDevelopmentStart {
    param([string]$TrackerSimulatorCom, [switch]$TrackerSimulatorOff)
    if ($TrackerSimulatorCom -and $TrackerSimulatorOff) {
        throw 'Choose either a simulator COM port or -TrackerSimulatorOff.'
    }
    if ($TrackerSimulatorCom -and $TrackerSimulatorCom -notmatch '\ACOM[1-9][0-9]{0,4}\z') {
        throw 'Select an explicit Windows COM port with -TrackerSimulatorCom, for example COM4.'
    }
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
    $script:EltsBuildDigest = $fingerprint
    $script:EltsBuildDecision = 'No cached build'
    $cachePath = Join-Path $root 'diagnostics/start-build.json'
    $player = $null
    if (Test-Path -LiteralPath $cachePath) {
        try {
            $cache = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
            $script:EltsBuildDecision = if ($cache.fingerprint -ne $fingerprint) { 'Source or staged configuration changed' } else { 'Cached player missing or invalid' }
            if ($cache.fingerprint -ne $fingerprint -and $cache.PSObject.Properties['groups'] -and $cache.groups) {
                $changed = @($script:EltsFingerprintGroups.Keys | Where-Object { $cache.groups.$_ -ne $script:EltsFingerprintGroups[$_] })
                if ($changed.Count) { $script:EltsBuildDecision = 'Changed: ' + ($changed -join ', ') }
            }
            # Accept only our own relative generated build directories.
            if ($cache.fingerprint -eq $fingerprint -and $cache.output -match '^build/start-elts-[a-f0-9]{32}$') {
                $candidate = Join-Path $root ($cache.output + '/ELTS-Synthetic.exe')
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    # An executable alone is insufficient: verify DLLs and data too.
                    Set-EltsStartupStatus 'Verifying the saved app' 'Checking the participant game and its required data files before opening it.'
                    Invoke-EltsSetupProcess $paths.python @('-X','utf8','-m',
                        'tools.build.verify', (Split-Path -Parent $candidate))
                    $player = $candidate
                    $script:EltsBuildDecision = 'Verified cached player'
                }
            }
        } catch {
            $script:EltsBuildDecision = 'Cached build verification failed'
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
            '-PythonExecutable', $paths.python, '-UnityEditor', $paths.unity, '-UseCalibrationProfile') -TimeoutSeconds 3600 `
            -Activity 'Build dashboard and participant game' -ReportPaths (Join-Path $root 'diagnostics/build-report.json') -RetryCommand 'START-ELTS.cmd'
        $player = Join-Path $root ($output + '/ELTS-Synthetic.exe')
        if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw 'Build finished without a dashboard executable.' }
        # A profile saved during the build must not get credited to an older player.
        if ((Get-EltsStartupFingerprint) -ne $fingerprint) {
            throw 'Source or calibration profiles changed during the build. Run START-ELTS.cmd again to build with the latest inputs.'
        }
        Set-EltsStartupStatus 'Saving build information' 'Recording the source fingerprint so later launches can reuse this build.'
        $cache = @{ fingerprint = $fingerprint; groups = $script:EltsFingerprintGroups; output = $output }
        [IO.File]::WriteAllText($cachePath, ($cache | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    }
    # Unity runs headlessly only when setup/build requires it. Open the Editor
    # manually for source work; ordinary testing needs just the two app windows.
    # Preserve the connected desktop at startup; fullscreen remains a user action.
    Set-EltsStartupStatus 'Opening ELTS' 'Starting the participant game. Its administrator dashboard will open in your browser.'
    # Keep each attempt's Unity diagnostics, including failed scene initialization.
    $playerLog = Join-Path $root ('diagnostics/player-start-' + [Guid]::NewGuid().ToString('N') + '.log')
    Write-Host "Player log: $playerLog"
    $playerArguments = @('-eltsWindowed','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',$playerLog)
    if ($TrackerSimulatorCom) { $playerArguments += @('-eltsTrackerSimulatorCom', $TrackerSimulatorCom.ToUpperInvariant()) }
    if ($TrackerSimulatorOff) { $playerArguments += '-eltsTrackerSimulatorOff' }
    $process = Start-EltsVisibleApp $player $playerArguments (Split-Path -Parent $player)
    try {
        Set-EltsStartupStatus 'Initializing the participant view' 'Waiting for the game scene and administrator server. An initialization error will appear here; first launches may take longer.'
        Wait-EltsPlayerReady -Process $process -LogPath $playerLog
    } finally { $process.Dispose() }
    Write-Host 'Participant view and administrator server initialized.' -ForegroundColor Green
}
