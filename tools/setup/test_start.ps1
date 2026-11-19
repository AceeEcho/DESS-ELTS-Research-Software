# Offline orchestration tests: no installers, editors, or real players launched.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'scripts/setup-tools.ps1')
. (Join-Path $repo 'scripts/start-dev-tools.ps1')
. (Join-Path $repo 'scripts/start-progress.ps1')
$originalLocation = Get-Location
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ELTS startup tests ' + [Guid]::NewGuid().ToString('N'))
$script:EltsRepositoryRoot = $fixture
$script:calls = New-Object 'System.Collections.Generic.List[string]'
$script:open = $false
$script:failSetup = $false
$script:failBuild = $false
$script:fingerprint = 'source-one'
$script:expectedTrackerPort = $null
$script:statuses = New-Object 'System.Collections.Generic.List[string]'
function Set-EltsStartupStatus {
    param($Title, $Detail, $Result)
    $script:statuses.Add($Title)
}
function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Get-EltsSetupPaths { return @{git=$null;python='python';dotnet='dotnet';node='node';unity='Unity.exe'} }
function Initialize-EltsLocalTools {}
function Test-EltsProjectOpen { return $script:open }
function Get-EltsStartupFingerprint { return $script:fingerprint }
function Start-EltsVisibleApp {
    param($File, $Arguments, $Directory)
    if ($File -eq 'Unity.exe') {
        Assert ($Arguments[1] -eq (Join-Path $fixture 'unity')) 'Unity project argument lost its space-containing path'
        $script:calls.Add('editor')
    } else {
        Assert ($Arguments -contains '-eltsWindowed') 'Player must suppress automatic display switching'
        $fullscreenIndex = [Array]::IndexOf($Arguments, '-screen-fullscreen')
        Assert ($fullscreenIndex -ge 0 -and $Arguments[$fullscreenIndex + 1] -eq '0') 'Player must start windowed before scene initialization'
        $logIndex = [Array]::IndexOf($Arguments, '-logFile')
        Assert ($logIndex -ge 0 -and $Arguments[$logIndex + 1] -like '*diagnostics*player-start-*.log') 'Each player needs an explicit startup log'
        $trackerIndex = [Array]::IndexOf($Arguments, '-eltsTrackerSimulatorCom')
        if ($script:expectedTrackerPort) {
            Assert ($trackerIndex -ge 0 -and $Arguments[$trackerIndex + 1] -ceq $script:expectedTrackerPort) 'Explicit simulator port lost at launch'
        } else {
            Assert ($trackerIndex -lt 0) 'Ordinary startup must leave USB disabled'
        }
        $script:calls.Add('dashboard')
        $process = New-Object PSObject
        $process | Add-Member ScriptMethod Dispose {}
        return $process
    }
}
function Wait-EltsPlayerReady {
    param($Process, $LogPath)
    Assert ($null -ne $Process -and $LogPath -like '*player-start-*.log') 'Wait must follow the launched process and its own log'
}
function Invoke-EltsSetupProcess {
    param($File, $Arguments, $TimeoutSeconds, $Activity, $ReportPaths, $RetryCommand)
    if (($Arguments -join ' ') -match 'setup-dev.ps1') {
        Assert ($ReportPaths -eq (Join-Path $fixture 'diagnostics/setup-report.json') -and $RetryCommand -eq 'START-ELTS.cmd') 'Startup must preserve setup diagnostics and its retry command'
        $script:calls.Add('setup')
        if ($script:failSetup) { throw 'fixture setup failed' }
        @{result='ready';toolchainSha256=(Get-FileHash (Join-Path $fixture 'config/toolchain.json')).Hash} |
            ConvertTo-Json | Set-Content (Join-Path $fixture 'diagnostics/setup-report.json')
    } elseif (($Arguments -join ' ') -match 'build.ps1') {
        Assert ($Arguments -contains '-UseCalibrationProfile') 'The launcher build must enable the profile handoff'
        Assert ($ReportPaths -eq (Join-Path $fixture 'diagnostics/build-report.json') -and $Activity -match 'Build') 'Startup must preserve build diagnostics and failed step'
        $script:calls.Add('build')
        if ($script:failBuild) { throw 'fixture build failed' }
        $output = $Arguments[[Array]::IndexOf($Arguments, '-Output') + 1]
        $directory = Join-Path $fixture $output
        New-Item -ItemType Directory -Force $directory | Out-Null
        Set-Content (Join-Path $directory 'ELTS-Synthetic.exe') 'fixture only'
    } else { $script:calls.Add('verify') }
}
try {
    New-Item -ItemType Directory -Force (Join-Path $fixture 'config'),(Join-Path $fixture 'diagnostics') | Out-Null
    Copy-Item (Join-Path $repo 'config/toolchain.json') (Join-Path $fixture 'config/toolchain.json')
    Invoke-EltsDevelopmentStart
    Assert (($calls -join ',') -eq 'setup,build,dashboard') 'First start must not open the Editor'
    Assert (($statuses -join ',') -eq 'Checking this computer,Preparing this computer,Checking application files,Building ELTS,Saving build information,Opening ELTS,Initializing the participant view') 'First launch must report setup/build stages in order'
    $statuses.Clear()
    $calls.Clear()
    Invoke-EltsDevelopmentStart
    Assert (($calls -join ',') -eq 'verify,dashboard') 'Repeat start must reuse the build without opening the Editor'
    Assert (($statuses -join ',') -eq 'Checking this computer,Checking application files,Verifying the saved app,Opening ELTS,Initializing the participant view') 'Cached launch must not claim to install or build'
    $calls.Clear(); $script:expectedTrackerPort = 'COM4'
    Invoke-EltsDevelopmentStart -TrackerSimulatorCom com4
    Assert (($calls -join ',') -eq 'verify,dashboard') 'The simulator choice must reuse the same cached player'
    $script:expectedTrackerPort = $null
    $calls.Clear()
    $blocked = $false
    try { Invoke-EltsDevelopmentStart -TrackerSimulatorCom auto } catch { $blocked = $_.Exception.Message -match 'explicit Windows COM' }
    Assert ($blocked -and $calls.Count -eq 0) 'An invalid simulator port must fail before startup'
    $calls.Clear(); $script:open = $true
    Invoke-EltsDevelopmentStart
    Assert (($calls -join ',') -eq 'verify,dashboard') 'Already-open editor duplicated'
    $calls.Clear(); $script:fingerprint = 'source-two'
    $blocked = $false
    try { Invoke-EltsDevelopmentStart } catch { $blocked = $_.Exception.Message -match 'Close this project' }
    Assert ($blocked -and $calls.Count -eq 0) 'Build attempted against open Unity project'
    $script:open = $false
    Invoke-EltsDevelopmentStart
    Assert (($calls -join ',') -eq 'build,dashboard') 'Source change must rebuild without opening the Editor'
    $calls.Clear(); $statuses.Clear(); $script:fingerprint = 'source-three'; $script:failBuild = $true
    try { Invoke-EltsDevelopmentStart } catch { Assert ($_.Exception.Message -eq 'fixture build failed') 'Wrong build failure' }
    Assert (($calls -join ',') -eq 'build') 'Apps opened after failed build'
    Assert ($statuses[$statuses.Count - 1] -eq 'Building ELTS') 'Build failure must not report a later startup stage'
    $calls.Clear(); $statuses.Clear(); $script:failSetup = $true
    Set-Content (Join-Path $fixture 'diagnostics/setup-report.json') '{}'
    try { Invoke-EltsDevelopmentStart } catch { Assert ($_.Exception.Message -eq 'fixture setup failed') 'Wrong setup failure' }
    Assert (($calls -join ',') -eq 'setup') 'Apps opened after failed setup'
    Assert ($statuses[$statuses.Count - 1] -eq 'Preparing this computer') 'Setup failure must not report a later startup stage'
    & {
        . (Join-Path $repo 'scripts/start-dev-tools.ps1')
        $previousStation = $env:ELTS_STATION_HOME
        try {
            $script:EltsRepositoryRoot = Join-Path $fixture 'Local/ELTS'
            $env:ELTS_STATION_HOME = $script:EltsRepositoryRoot
            foreach ($folder in @('unity/Assets','unity/Packages','unity/ProjectSettings','config/local',
                    'schemas','tools/config','tools/build','tools/bootstrap','scripts')) {
                New-Item -ItemType Directory -Force (Join-Path $script:EltsRepositoryRoot $folder) | Out-Null
            }
            $asset = Join-Path $script:EltsRepositoryRoot 'unity/Assets/test.txt'
            Set-Content -LiteralPath $asset 'first'
            $first = Get-EltsStartupFingerprint
            Set-Content -LiteralPath $asset 'other'
            $changed = Get-EltsStartupFingerprint
            Assert ($first -ne $changed) 'An installation under a Local folder must detect edited asset contents'
            Set-Content -LiteralPath (Join-Path $script:EltsRepositoryRoot 'config/local/tools.json') 'temporary cache'
            Assert ((Get-EltsStartupFingerprint) -eq $changed) 'Local setup cache must not invalidate the build'
        } finally { $script:EltsRepositoryRoot = $fixture; $env:ELTS_STATION_HOME = $previousStation }
    }
    Write-Host 'PASS: startup scenarios without Git, including optional simulator COM selection.'
} finally {
    Set-Location $originalLocation
    # The only recursive deletion is the exact temporary fixture created above.
    if ([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
