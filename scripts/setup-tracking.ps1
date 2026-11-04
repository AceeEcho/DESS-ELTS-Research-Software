<#
.SYNOPSIS
Prepare headset-free VIVE tracking from an extracted ELTS ZIP. Safe to rerun.
.DESCRIPTION
Installs only tracking prerequisites, not Unity. Steam login/install dialogs and
physical pairing remain interactive. -Check detects readiness without changes.
#>
[CmdletBinding()]
param([switch]$Check, [string]$SteamPath, [string]$SteamVRPath, [string]$PythonExecutable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'setup-tools.ps1')
$root = $script:EltsRepositoryRoot
$calibration = Join-Path $root 'Calibration Environment'
$manifest = Get-Content -LiteralPath (Join-Path $root 'config/toolchain.json') -Raw | ConvertFrom-Json
$toolRoot = Join-Path $env:LOCALAPPDATA 'ELTS/Tools'
$localPython = Join-Path $calibration '.venv/Scripts/python.exe'
$helper = Join-Path $root 'tools/bootstrap/tracking_setup.py'
$transcribing = $false
$setupLock = $null
$exitCode = 1

function Invoke-TrackingHelper {
    param([string[]]$Options)
    $arguments = @($helper) + $Options
    if ($SteamPath) { $arguments += @('--steam', $SteamPath) }
    if ($SteamVRPath) { $arguments += @('--runtime', $SteamVRPath) }
    $output = & $script:trackingPython @arguments
    $code = $LASTEXITCODE
    $result = ($output -join "`n") | ConvertFrom-Json
    if ($code -ne 0) {
        if ($result.PSObject.Properties['error']) { throw $result.error }
        throw 'Headset-free settings are not configured. Run START-CALIBRATION.cmd and answer Y to set up live tracking.'
    }
    return $result
}

try {
    if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'Tracking setup requires Windows x64 on an Intel/AMD computer.'
    }
    Assert-EltsSetupManifest $manifest
    if (-not $Check) {
        New-Item -ItemType Directory -Force (Join-Path $root 'diagnostics'), $toolRoot | Out-Null
        # Share the prerequisite lock with ELTS developer setup to avoid competing installs.
        $setupLock = [IO.File]::Open((Join-Path $toolRoot 'setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
        Start-Transcript -Path (Join-Path $root 'diagnostics/tracking-setup.log') -Force | Out-Null
        $transcribing = $true
    }
    Write-Host 'ELTS tracker setup - Python, OpenVR reader, SteamVR and headset-free settings.'
    $paths = Get-EltsSetupPaths $manifest $toolRoot $PythonExecutable ''
    $script:trackingPython = $paths.python
    if (-not $script:trackingPython) {
        if ($Check) { throw 'Python is missing. Run START-CALIBRATION.cmd to install it.' }
        $pins = $manifest.windowsSetup
        $name = "python-$($pins.pythonVersion)-amd64.exe"
        $installer = Get-EltsVerifiedDownload "https://www.python.org/ftp/python/$($pins.pythonVersion)/$name" `
            (Join-Path $toolRoot "downloads/$name") $pins.pythonSha256
        $destination = Join-Path $toolRoot "python/$($pins.pythonVersion)"
        Invoke-EltsSetupProcess $installer @('/quiet','InstallAllUsers=0',"TargetDir=$destination",'PrependPath=0',
            'Include_launcher=0','Include_test=0','Include_doc=0','Include_tcltk=0','Include_pip=1','AssociateFiles=0') `
            -SuccessCodes @(0,3010) -Activity 'Install Python for tracking' -RetryCommand 'START-CALIBRATION.cmd'
        $script:trackingPython = Join-Path $destination 'python.exe'
        if (-not (Test-EltsPython $script:trackingPython $manifest)) { throw 'Python installation could not be verified.' }
    }
    if (-not $Check) {
        # Recreate missing machine-local executables when a copied .venv is unusable.
        # No environment or profile directory is deleted.
        if (-not (Test-EltsPython $localPython $manifest)) {
            Invoke-EltsSetupProcess $script:trackingPython @('-m','venv',(Join-Path $calibration '.venv')) `
                -Activity 'Create tracking environment' -RetryCommand 'START-CALIBRATION.cmd'
        }
        Invoke-EltsSetupProcess $localPython @('-m','pip','install','--disable-pip-version-check','-r',
            (Join-Path $calibration 'requirements-live.txt')) -Activity 'Install OpenVR reader' -RetryCommand 'START-CALIBRATION.cmd'
    }
    if (-not (Test-EltsPython $localPython $manifest)) { throw 'Tracking Python environment is missing or unusable. Run START-CALIBRATION.cmd.' }
    $expected = ((Get-Content -LiteralPath (Join-Path $calibration 'requirements-live.txt') -Raw).Trim() -split '==')[1]
    & $localPython -B -c "import openvr, importlib.metadata, sys; assert importlib.metadata.version('openvr') == sys.argv[1]" $expected
    if ($LASTEXITCODE -ne 0) { throw 'Pinned OpenVR reader could not be imported. Rerun START-CALIBRATION.cmd.' }
    $found = Invoke-TrackingHelper @('--discover')
    if (-not $found.steam) {
        if ($Check) { throw 'Steam is missing. Run START-CALIBRATION.cmd.' }
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if ($winget) {
            Invoke-EltsSetupProcess $winget.Source @('install','--id','Valve.Steam','--exact','--source','winget',
                '--accept-source-agreements','--accept-package-agreements') `
                -Activity 'Install Steam' -RetryCommand 'START-CALIBRATION.cmd'
        } else {
            # Fallback for PCs without App Installer/winget: authenticate Valve's
            # official installer before showing its normal installation wizard.
            $installer = Join-Path $toolRoot 'SteamSetup.exe'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest 'https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe' -OutFile $installer -UseBasicParsing
            $signature = Get-AuthenticodeSignature -LiteralPath $installer
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Valve Corp') {
                throw 'The Steam installer does not have a valid Valve publisher signature. It was not run.'
            }
            Invoke-EltsSetupProcess $installer -Activity 'Install Steam' -RetryCommand 'START-CALIBRATION.cmd'
        }
        $found = Invoke-TrackingHelper @('--discover')
        if (-not $found.steam) { throw 'Finish Steam installation, then rerun START-CALIBRATION.cmd.' }
    }
    if (-not $found.runtime) {
        if ($Check) { throw 'SteamVR is missing or incomplete. Run START-CALIBRATION.cmd.' }
        Write-Host 'Steam will open the SteamVR installer. Sign in if asked, click Install, and wait for the download to finish.'
        # A visible window is intentional: Steam authentication and installation
        # confirmations must be completed in the user's own account.
        Start-Process -FilePath (Join-Path $found.steam 'steam.exe') -ArgumentList 'steam://install/250820'
        $null = Read-Host 'When SteamVR finishes installing, press Enter here to continue'
        $found = Invoke-TrackingHelper @('--discover')
        if (-not $found.runtime) { throw 'SteamVR installation is not complete. Finish it in Steam, then rerun START-CALIBRATION.cmd.' }
    }
    if ($Check) {
        $result = Invoke-TrackingHelper @('--check')
        if (-not $found.registeredRuntime -or -not $found.registeredConfig -or -not $found.registeredLog) {
            throw 'OpenVR paths need registration. Run START-CALIBRATION.cmd.'
        }
    } else {
        if (@(Get-Process vrserver,vrmonitor,vrcompositor -ErrorAction SilentlyContinue).Count -gt 0) {
            Write-Host 'Close SteamVR completely so its settings can be configured. Steam itself may stay open.'
            $null = Read-Host 'Press Enter after closing SteamVR'
            if (@(Get-Process vrserver,vrmonitor,vrcompositor -ErrorAction SilentlyContinue).Count -gt 0) {
                throw 'SteamVR is still running. Close it and rerun START-CALIBRATION.cmd.'
            }
        }
        if (-not $found.registeredRuntime -or -not $found.registeredConfig -or -not $found.registeredLog) {
            New-Item -ItemType Directory -Force (Split-Path -Parent $found.openvrPaths) | Out-Null
            if (Test-Path -LiteralPath $found.openvrPaths) {
                $backup = $found.openvrPaths + '.before-elts-tracking-' + (Get-Date -Format 'yyyyMMddHHmmssffff') + '.bak'
                Copy-Item -LiteralPath $found.openvrPaths -Destination $backup
                Write-Host "OpenVR paths backup: $backup"
            }
            $pathreg = Join-Path $found.runtime 'bin/win64/vrpathreg.exe'
            if (-not $found.registeredRuntime) {
                & $pathreg setruntime $found.runtime
                if ($LASTEXITCODE -ne 0) { throw 'Could not register the SteamVR runtime.' }
            }
            if (-not $found.registeredConfig) {
                $configDirectory = Split-Path -Parent $found.config
                New-Item -ItemType Directory -Force $configDirectory | Out-Null
                & $pathreg setconfig $configDirectory
                if ($LASTEXITCODE -ne 0) { throw 'Could not register the SteamVR config directory.' }
            }
            if (-not $found.registeredLog) {
                $logDirectory = Join-Path $found.steam 'logs'
                New-Item -ItemType Directory -Force $logDirectory | Out-Null
                & $pathreg setlog $logDirectory
                if ($LASTEXITCODE -ne 0) { throw 'Could not register the SteamVR log directory.' }
            }
        }
        $result = Invoke-TrackingHelper @()
        if ($result.backup) { Write-Host "Original settings backup: $($result.backup)" }
        $null = Invoke-TrackingHelper @('--check')
    }
    Write-Host "SteamVR: $($found.runtime)"
    Write-Host "Settings: $($result.config)"
    Write-Host 'Tracking software setup verified. Physical pairing and alignment have not been checked.' -ForegroundColor Green
    Write-Host 'Next: power the base station, connect dongles, start SteamVR and pair each tracker.'
    Write-Host 'Then restart START-CALIBRATION.cmd. Use Calibrate head directions and save a profile for this machine.'
    $exitCode = 0
} catch {
    Write-Host ("TRACKING SETUP NEEDS ATTENTION: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Correct the step above, then run START-CALIBRATION.cmd again. Existing calibration profiles are preserved.'
    if ($transcribing) { Write-Host ("Log: " + (Join-Path $root 'diagnostics/tracking-setup.log')) }
} finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
    if ($setupLock) { $setupLock.Dispose() }
}
exit $exitCode
