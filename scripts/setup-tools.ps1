# Windows setup helpers. Keep detection separate from installation so -Check and
# regression tests can inspect a machine without downloading or changing tools.
Set-StrictMode -Version Latest

function Assert-EltsSetupManifest {
    param($Manifest)
    # Validate the path-bearing fields before a Python/schema validator exists.
    foreach ($value in @($Manifest.windowsSetup.pythonVersion, $Manifest.windowsSetup.nodeVersion,
            $Manifest.standaloneTests.dotnetSdkVersion)) {
        if ($value -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid setup tool version in config/toolchain.json.' }
    }
    if ($Manifest.unity.version -notmatch '^6000\.3\.23f1$' -or $Manifest.unity.revision -notmatch '^[a-f0-9]{12}$' -or
            $Manifest.optionalUnityCli.testedVersion -notmatch '^\d+\.\d+\.\d+(?:-beta\.\d+)?$') {
        throw 'Invalid Unity version/revision in config/toolchain.json.'
    }
    if ([version]$Manifest.windowsSetup.pythonVersion -lt [version]$Manifest.python.minimumVersion -or
            [version]$Manifest.windowsSetup.pythonVersion -ge [version]$Manifest.python.maximumExclusiveVersion) {
        throw 'The Python installer pin is outside the supported range.'
    }
}

function Write-EltsToolPaths {
    param([string]$Path, $Paths)
    # The generated cache can be replaced, but a partial write must not destroy
    # the last usable file. NullString avoids PowerShell 5.1 coercing a null
    # backup filename into an invalid empty path for File.Replace.
    $temporary = $Path + ".tmp-$PID"
    [IO.File]::WriteAllText($temporary, ($Paths | ConvertTo-Json) + "`n", (New-Object Text.UTF8Encoding $false))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $Path) }
}

function ConvertTo-EltsProcessArgument {
    param([AllowEmptyString()][string]$Value)
    # ProcessStartInfo on Windows PowerShell 5.1 needs a command-line string.
    # Escape according to Windows argv rules, including a trailing backslash.
    '"' + ([regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1')) + '"'
}

function Invoke-EltsSetupProcess {
    param([string]$File, [string[]]$Arguments = @(), [int]$TimeoutSeconds = 3600,
          [int[]]$SuccessCodes = @(0))
    $argumentLine = ($Arguments | ForEach-Object { ConvertTo-EltsProcessArgument $_ }) -join ' '
    $process = Start-Process -FilePath $File -ArgumentList $argumentLine -NoNewWindow -PassThru
    # Windows PowerShell can lose the native exit code after a short-lived
    # process exits unless its handle is retained before WaitForExit.
    $null = $process.Handle
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # Do not kill an installer mid-write. The operator can inspect it safely.
        throw "Timed out waiting for $File. Check the running process before rerunning setup."
    }
    $process.WaitForExit()
    if ($process.ExitCode -notin $SuccessCodes) {
        throw "$File exited with code $($process.ExitCode). Resolve the displayed error and rerun SETUP-DEV.cmd."
    }
    if ($process.ExitCode -eq 3010) { throw 'Installation requires a Windows restart. Restart, then run SETUP-DEV.cmd again.' }
}

function Get-EltsCommandPath {
    param([string]$Name)
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command -and $command.Source -notlike '*\WindowsApps\*') { return $command.Source }
}

function Test-EltsPython {
    param([string]$Path, $Manifest)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $raw = & $Path -I -c 'import platform; print(platform.python_version())' 2>$null
        if ($LASTEXITCODE -ne 0) { return $false }
        $version = [version]([string]($raw | Select-Object -Last 1))
        return $version -ge [version]$Manifest.python.minimumVersion -and $version -lt [version]$Manifest.python.maximumExclusiveVersion
    } catch { return $false }
}

function Test-EltsDotnet {
    param([string]$Path, $Manifest)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $sdks = @(& $Path --list-sdks 2>$null)
        return $LASTEXITCODE -eq 0 -and @($sdks | Where-Object { $_ -match ('^' + [regex]::Escape($Manifest.standaloneTests.dotnetSdkVersion) + ' \[') }).Count -gt 0
    } catch { return $false }
}

function Test-EltsNode {
    param([string]$Path, $Manifest)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        $raw = & $Path --version 2>$null
        return $LASTEXITCODE -eq 0 -and ([string]$raw).Trim() -eq ('v' + $Manifest.windowsSetup.nodeVersion)
    } catch { return $false }
}

function Test-EltsEditor {
    param([string]$Path, $Manifest)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $version = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion -split '_'
    return $version[0] -eq $Manifest.unity.version
}

function Get-EltsSetupPaths {
    param($Manifest, [string]$ToolRoot, [string]$PythonExecutable, [string]$UnityEditor)
    $result = [ordered]@{ python = $null; dotnet = $null; node = $null; git = $null; unity = $null; unityCli = $null }
    $pythonCandidates = @($PythonExecutable, $env:ELTS_PYTHON, (Get-EltsCommandPath 'python'),
        (Join-Path $ToolRoot "python/$($Manifest.windowsSetup.pythonVersion)/python.exe"))
    # Python's traditional installer and the install manager both have a launcher.
    $launcher = Get-EltsCommandPath 'py'
    if ($launcher) {
        try {
            $launched = & $launcher -3 -c 'import sys; print(sys.executable)' 2>$null
            if ($LASTEXITCODE -eq 0) { $pythonCandidates += $launched }
        } catch { }
    }
    $pythonCandidates += @(Get-ChildItem -Path "$env:LOCALAPPDATA/Programs/Python/Python*/python.exe" -ErrorAction SilentlyContinue | ForEach-Object FullName)
    foreach ($candidate in $pythonCandidates) {
        if (Test-EltsPython $candidate $Manifest) { $result.python = $candidate; break }
    }
    foreach ($candidate in @((Get-EltsCommandPath 'dotnet'), (Join-Path $ToolRoot 'dotnet/dotnet.exe'),
            "$env:ProgramFiles/dotnet/dotnet.exe", "$env:USERPROFILE/.dotnet/dotnet.exe")) {
        if (Test-EltsDotnet $candidate $Manifest) { $result.dotnet = $candidate; break }
    }
    foreach ($candidate in @((Get-EltsCommandPath 'node'), "$env:ProgramFiles/nodejs/node.exe",
            (Join-Path $ToolRoot "node/node-v$($Manifest.windowsSetup.nodeVersion)-win-x64/node.exe"))) {
        if (Test-EltsNode $candidate $Manifest) { $result.node = $candidate; break }
    }
    $gitCandidates = @((Get-EltsCommandPath 'git'), "$env:ProgramFiles/Git/cmd/git.exe")
    $gitCandidates += @(Get-ChildItem -Path "$env:LOCALAPPDATA/GitHubDesktop/app-*/resources/app/git/cmd/git.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | ForEach-Object FullName)
    foreach ($candidate in $gitCandidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $raw = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $raw -match 'git version (\d+\.\d+\.\d+)' -and
                    [version]$Matches[1] -ge [version]$Manifest.git.minimumVersion) { $result.git = $candidate; break }
        }
    }
    foreach ($candidate in @((Join-Path $ToolRoot "unity-cli/$($Manifest.optionalUnityCli.testedVersion)/unity.exe"),
            "$env:LOCALAPPDATA/Unity/bin/unity.exe", (Get-EltsCommandPath 'unity'))) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $raw = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and ([string]$raw).Trim() -eq $Manifest.optionalUnityCli.testedVersion) { $result.unityCli = $candidate; break }
        }
    }
    $editorCandidates = @($UnityEditor, $env:ELTS_UNITY_EDITOR,
        "$env:ProgramFiles/Unity/Hub/Editor/$($Manifest.unity.version)/Editor/Unity.exe")
    if ($result.unityCli) {
        try {
            $raw = & $result.unityCli editors path $Manifest.unity.version --json 2>$null
            if ($LASTEXITCODE -eq 0) {
                $location = ($raw -join "`n") | ConvertFrom-Json
                if ($location.success) { $editorCandidates += Join-Path $location.data.path 'Editor/Unity.exe' }
            }
        } catch { }
    }
    foreach ($candidate in $editorCandidates) {
        if (Test-EltsEditor $candidate $Manifest) { $result.unity = $candidate; break }
    }
    return $result
}

function Get-EltsVerifiedDownload {
    param([string]$Url, [string]$Destination, [string]$Hash, [ValidateSet('SHA256','SHA512')][string]$Algorithm = 'SHA256')
    if ([uri]$Url -and ([uri]$Url).Scheme -ne 'https') { throw 'Setup downloads require HTTPS.' }
    if ($Hash -notmatch '^[a-fA-F0-9]+$' -or $Hash.Length -ne $(if ($Algorithm -eq 'SHA512') {128} else {64})) {
        throw 'Invalid pinned installer checksum. Check config/toolchain.json.'
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Destination) | Out-Null
    if ((Test-Path -LiteralPath $Destination -PathType Leaf) -and
            (Get-FileHash -LiteralPath $Destination -Algorithm $Algorithm).Hash -eq $Hash) { return $Destination }
    # Publish only a complete, verified download. An interrupted transfer is not reused.
    $partial = $Destination + '.partial'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
    if ((Get-FileHash -LiteralPath $partial -Algorithm $Algorithm).Hash -ne $Hash) {
        throw "Download checksum mismatch: $Url. The installer was not executed."
    }
    Move-Item -LiteralPath $partial -Destination $Destination -Force
    return $Destination
}

function Install-EltsMissingTools {
    param($Paths, $Manifest, [string]$ToolRoot)
    $cache = Join-Path $ToolRoot 'downloads'
    $pins = $Manifest.windowsSetup
    if (-not $Paths.python) {
        Write-Host "Installing Python $($pins.pythonVersion) for this Windows account..."
        $name = "python-$($pins.pythonVersion)-amd64.exe"
        $file = Get-EltsVerifiedDownload "https://www.python.org/ftp/python/$($pins.pythonVersion)/$name" (Join-Path $cache $name) $pins.pythonSha256
        $destination = Join-Path $ToolRoot "python/$($pins.pythonVersion)"
        Invoke-EltsSetupProcess $file @('/quiet', 'InstallAllUsers=0', "TargetDir=$destination", 'PrependPath=0',
            'Include_launcher=0', 'Include_test=0', 'Include_doc=0', 'Include_tcltk=0', 'Include_pip=1', 'AssociateFiles=0') -SuccessCodes @(0,3010)
    }
    if (-not $Paths.dotnet) {
        $version = $Manifest.standaloneTests.dotnetSdkVersion
        Write-Host "Installing .NET SDK $version into the ELTS tools folder..."
        $name = "dotnet-sdk-$version-win-x64.zip"
        $file = Get-EltsVerifiedDownload "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/$name" (Join-Path $cache $name) $pins.dotnetSha512 SHA512
        Expand-Archive -LiteralPath $file -DestinationPath (Join-Path $ToolRoot 'dotnet') -Force
    }
    if (-not $Paths.node) {
        Write-Host "Installing Node.js $($pins.nodeVersion) for the browser simulation..."
        $name = "node-v$($pins.nodeVersion)-win-x64.zip"
        $file = Get-EltsVerifiedDownload "https://nodejs.org/dist/v$($pins.nodeVersion)/$name" (Join-Path $cache $name) $pins.nodeSha256
        Expand-Archive -LiteralPath $file -DestinationPath (Join-Path $ToolRoot 'node') -Force
    }
    if (-not $Paths.unity -and -not $Paths.unityCli) {
        $version = $Manifest.optionalUnityCli.testedVersion
        Write-Host "Installing Unity CLI $version to install the selected Editor..."
        $destination = Join-Path $ToolRoot "unity-cli/$version/unity.exe"
        $null = Get-EltsVerifiedDownload "https://public-cdn.cloud.unity3d.com/hub/prod/cli/$version/unity-windows-x64.exe" $destination $pins.unityCliSha256
    }
}
