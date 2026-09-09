<#
.SYNOPSIS
Prepare an editable Windows x64 ELTS clone. Safe to rerun after a pull or restart.
.DESCRIPTION
Installs missing tools from checksum-pinned official downloads, imports the Unity
project, and runs verification. Does not push, change branches, or approve a study.
-Check only detects tools. -SkipVerification prepares/imports but does not claim
verification. Explicit executable paths support existing custom installations.
#>
[CmdletBinding()]
param([switch]$Check, [switch]$SkipVerification, [string]$PythonExecutable, [string]$UnityEditor)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'setup-tools.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $script:EltsRepositoryRoot 'config/toolchain.json') -Raw | ConvertFrom-Json
$toolRoot = Join-Path $env:LOCALAPPDATA 'ELTS/Tools'
$reportDirectory = Join-Path $script:EltsRepositoryRoot 'diagnostics'
$reportPath = Join-Path $reportDirectory 'setup-report.json'
$report = [ordered]@{ result = 'failed'; checkedAtUtc = [DateTime]::UtcNow.ToString('o'); phase = 'detect'; tools = $null;
    message = ''; studyReady = $false; verificationRun = $false; sourceRevision = $null;
    toolchainSha256 = (Get-FileHash -LiteralPath (Join-Path $script:EltsRepositoryRoot 'config/toolchain.json') -Algorithm SHA256).Hash }
$transcribing = $false
$installationLock = $null
try {
    Assert-EltsSetupManifest $manifest
    if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'This setup supports Windows x64. Use 64-bit Windows PowerShell on an Intel/AMD Windows PC.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $script:EltsRepositoryRoot '.git'))) {
        throw 'This folder is not a Git clone. Clone with GitHub Desktop, then run SETUP-DEV.cmd from that clone.'
    }
    New-Item -ItemType Directory -Force $reportDirectory | Out-Null
    Start-Transcript -Path (Join-Path $reportDirectory 'setup-console.log') -Force | Out-Null
    $transcribing = $true
    Write-Host 'ELTS development setup: one editable repository on each computer.'
    Write-Host 'Close this project in Unity before continuing. Setup may take several minutes.'
    if ($PythonExecutable -and -not (Test-EltsPython $PythonExecutable $manifest)) { throw 'The supplied PythonExecutable is missing or outside the supported version range.' }
    if ($UnityEditor -and -not (Test-EltsEditor $UnityEditor $manifest)) { throw 'The supplied UnityEditor is missing or is not the exact pinned version.' }
    $paths = Get-EltsSetupPaths $manifest $toolRoot $PythonExecutable $UnityEditor
    $report.tools = $paths
    if ($paths.git) {
        $revision = & $paths.git -C $script:EltsRepositoryRoot rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Git could not read this clone. Check its ownership/access in GitHub Desktop.' }
        $report.sourceRevision = ([string]$revision).Trim()
    }
    foreach ($name in @('git','python','dotnet','node','unity')) {
        Write-Host ("{0}: {1}" -f $name, $(if ($paths[$name]) {$paths[$name]} else {'not found / wrong version'}))
    }
    if ($Check) {
        $missing = @('git','python','dotnet','node','unity') | Where-Object { -not $paths[$_] }
        if ($missing) { throw ('Missing tools: ' + ($missing -join ', ') + '. Run SETUP-DEV.cmd to prepare this machine.') }
        $report.result = 'tools-detected'; $report.message = 'Tools detected only. Import, tests and license operation were not checked.'
    } else {
        if (-not $paths.git) { throw 'Install GitHub Desktop or Git for Windows, reopen this folder, and rerun SETUP-DEV.cmd.' }
        $report.phase = 'install'
        New-Item -ItemType Directory -Force $toolRoot | Out-Null
        try { $installationLock = [IO.File]::Open((Join-Path $toolRoot 'setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
        catch { throw 'Another ELTS setup may be using this account''s tools folder. Let it finish before retrying.' }
        Install-EltsMissingTools $paths $manifest $toolRoot
        $paths = Get-EltsSetupPaths $manifest $toolRoot $PythonExecutable $UnityEditor
        if (-not $paths.unity) {
            if (-not $paths.unityCli) { throw 'Unity CLI is unavailable after installation. See the setup guide; do not substitute a different Editor version.' }
            # Hub provides the normal account/license UI. The CLI verifies its
            # official installer signature and reuses an existing Hub installation.
            Invoke-EltsSetupProcess $paths.unityCli @('hub', 'install')
            Write-Host 'Unity may ask you to review license terms or approve installation. Answer in this window.'
            # Windows Mono build support is bundled with the Windows Editor.
            # Do not auto-accept a license, upgrade an Editor, or install IL2CPP.
            Invoke-EltsSetupProcess $paths.unityCli @('install', $manifest.unity.version, '--changeset', $manifest.unity.revision)
            $paths = Get-EltsSetupPaths $manifest $toolRoot $PythonExecutable $UnityEditor
        }
        foreach ($name in @('git','python','dotnet','node','unity')) {
            if (-not $paths[$name]) { throw "$name is unavailable after installation. Inspect diagnostics/setup-console.log and rerun after resolving it." }
        }
        $report.tools = $paths
        # These paths are private to this clone and ignored by Git. No scripts,
        # credentials, rig calibration or study settings are stored here.
        $localDirectory = Join-Path $script:EltsRepositoryRoot 'config/local'
        New-Item -ItemType Directory -Force $localDirectory | Out-Null
        $localPath = Join-Path $localDirectory 'development-tools.json'
        Write-EltsToolPaths $localPath $paths
        # Detection (including explicit overrides) is authoritative for this run;
        # a stale inherited environment must not select an old machine's tools.
        $env:ELTS_PYTHON = $paths.python
        $env:ELTS_UNITY_EDITOR = $paths.unity
        Initialize-EltsLocalTools
        $shell = Get-EltsCommandPath 'powershell.exe'
        if (-not $shell) { $shell = Get-EltsCommandPath 'pwsh.exe' }
        if (-not $shell) { throw 'A PowerShell executable is required to run the development wrappers.' }
        $report.phase = 'bootstrap'
        Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File', (Join-Path $PSScriptRoot 'bootstrap-dev.ps1'))
        $report.phase = 'unity-import'
        Write-Host 'Importing Unity packages. If licensing fails, sign in and activate your eligible license in Unity Hub, then rerun setup.'
        $importLog = Join-Path $reportDirectory 'setup-unity-import.log'
        Invoke-EltsSetupProcess $paths.unity @('-batchmode','-nographics','-quit','-projectPath',
            (Join-Path $script:EltsRepositoryRoot $manifest.unity.projectPath), '-logFile', $importLog)
        if (-not (Test-Path -LiteralPath $importLog -PathType Leaf)) { throw 'Unity did not create the import log.' }
        $report.phase = 'verify'
        if ($SkipVerification) {
            $report.result = 'prepared-unverified'; $report.message = 'Tools, configuration and import prepared; verification was explicitly skipped.'
        } else {
            Invoke-EltsSetupProcess $shell @('-NoProfile','-ExecutionPolicy','Bypass','-File', (Join-Path $PSScriptRoot 'verify.ps1')) -TimeoutSeconds 7200
            $report.verificationRun = $true
            $report.result = 'ready'; $report.message = 'Development setup and verification passed. Ready to edit; no physical/study acceptance.'
        }
    }
    Write-Host $report.message -ForegroundColor Green
} catch {
    $report.message = $_.Exception.Message
    Write-Host ('SETUP STOPPED: ' + $report.message) -ForegroundColor Red
} finally {
    if ($installationLock) { $installationLock.Dispose() }
    New-Item -ItemType Directory -Force $reportDirectory | Out-Null
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 6) + "`n", (New-Object Text.UTF8Encoding $false))
    if ($transcribing) { Stop-Transcript | Out-Null }
}
if ($report.result -eq 'failed') { exit 1 }
exit 0
