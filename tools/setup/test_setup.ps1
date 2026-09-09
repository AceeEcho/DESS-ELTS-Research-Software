<#!
.SYNOPSIS
    Offline regression tests for the PowerShell 5.1 ELTS setup helpers.
.DESCRIPTION
    This file intentionally does not use Pester. It dot-sources the helper
    functions, replaces download/install/archive commands with recording mocks,
    and uses a temporary directory for all fixtures. It never installs tools or
    contacts a network. Pass the bundled Python path with -PythonExecutable when
    available to exercise the real supported-version probe.
#>
[CmdletBinding()]
param([string]$PythonExecutable)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'scripts/common.ps1')
. (Join-Path $repo 'scripts/setup-tools.ps1')
$realInvokeEltsSetupProcess = (Get-Command Invoke-EltsSetupProcess -CommandType Function).ScriptBlock
$realGetEltsVerifiedDownload = (Get-Command Get-EltsVerifiedDownload -CommandType Function).ScriptBlock
$manifest = Get-Content -LiteralPath (Join-Path $repo 'config/toolchain.json') -Raw | ConvertFrom-Json
$failures = New-Object System.Collections.Generic.List[string]
$passes = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:failures.Add($Message) }
    else { $script:passes++ }
}
function Assert-Equal($Actual, $Expected, [string]$Message) {
    Assert-True (($Actual -join "`n") -eq ($Expected -join "`n")) ("{0} (actual: {1}; expected: {2})" -f $Message,($Actual -join ','),($Expected -join ','))
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('elts-setup-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$downloadCalls = New-Object System.Collections.ArrayList
$processCalls = New-Object System.Collections.ArrayList
$archiveCalls = New-Object System.Collections.ArrayList
function Get-EltsVerifiedDownload {
    param([string]$Url,[string]$Destination,[string]$Hash,[string]$Algorithm='SHA256')
    [void]$script:downloadCalls.Add([pscustomobject]@{Url=$Url; Destination=$Destination; Hash=$Hash; Algorithm=$Algorithm})
    return $Destination
}
function Invoke-EltsSetupProcess {
    param([string]$File,[string[]]$Arguments,[int[]]$SuccessCodes)
    [void]$script:processCalls.Add([pscustomobject]@{File=$File; Arguments=$Arguments; SuccessCodes=$SuccessCodes})
}
function Expand-Archive {
    param([string]$LiteralPath,[string]$DestinationPath,[switch]$Force)
    [void]$script:archiveCalls.Add([pscustomobject]@{LiteralPath=$LiteralPath; DestinationPath=$DestinationPath})
}

try {
    $localCache = Join-Path $temp 'paths.json'
    Write-EltsToolPaths $localCache @{python='first'}
    Assert-Equal (Get-Content $localCache -Raw | ConvertFrom-Json).python 'first' 'tool path cache is created on first setup'
    Write-EltsToolPaths $localCache @{python='second'}
    Assert-Equal (Get-Content $localCache -Raw | ConvertFrom-Json).python 'second' 'tool path cache is replaced on a second setup'
    Assert-True (-not (Test-Path -LiteralPath ($localCache + ".tmp-$PID"))) 'atomic path replacement leaves no partial file'
    # An all-present machine is a no-op: no downloader, archive extractor, or
    # installer process should be reached.
    $present = [pscustomobject]@{python='p'; dotnet='d'; node='n'; unity='u'; unityCli='c'; git='g'}
    Install-EltsMissingTools $present $manifest (Join-Path $temp 'tools')
    Assert-Equal $downloadCalls.Count 0 'installed tools do not download'
    Assert-Equal $processCalls.Count 0 'installed tools do not execute installers'
    Assert-Equal $archiveCalls.Count 0 'installed tools do not extract archives'

    # Exercise every missing pinned download and preserve its URL, destination,
    # checksum, and algorithm. Each case has only one missing tool.
    $cases = @(
        @{Name='python'; Hash=$manifest.windowsSetup.pythonSha256; Algorithm='SHA256'; UrlPart='/ftp/python/'; FilePart='python-3.13.15-amd64.exe'},
        @{Name='dotnet'; Hash=$manifest.windowsSetup.dotnetSha512; Algorithm='SHA512'; UrlPart='/Sdk/'; FilePart='dotnet-sdk-10.0.400-win-x64.zip'},
        @{Name='node'; Hash=$manifest.windowsSetup.nodeSha256; Algorithm='SHA256'; UrlPart='/dist/v24.21.0/'; FilePart='node-v24.21.0-win-x64.zip'},
        @{Name='unity'; Hash=$manifest.windowsSetup.unityCliSha256; Algorithm='SHA256'; UrlPart='/cli/'; FilePart='unity-windows-x64.exe'}
    )
    foreach ($case in $cases) {
        $downloadCalls.Clear(); $processCalls.Clear(); $archiveCalls.Clear()
        $missing = [pscustomobject]@{python='p'; dotnet='d'; node='n'; unity=$null; unityCli='c'; git='g'}
        $missing.($case.Name) = $null
        if ($case.Name -eq 'unity') { $missing.unityCli = $null }
        Install-EltsMissingTools $missing $manifest (Join-Path $temp $case.Name)
        Assert-Equal $downloadCalls.Count 1 ($case.Name + ' missing tool downloads once')
        if ($downloadCalls.Count -eq 1) {
            $call = $downloadCalls[0]
            Assert-True ($call.Hash -eq $case.Hash) ($case.Name + ' uses pinned checksum')
            Assert-True ($call.Algorithm -eq $case.Algorithm) ($case.Name + ' uses correct checksum algorithm')
            Assert-True ($call.Url -match [regex]::Escape($case.UrlPart) -and $call.Url -match [regex]::Escape($case.FilePart)) ($case.Name + ' uses pinned official URL')
        }
    }

    # Restore the actual downloader: only network IO is mocked from here, so
    # hashing, cache validation, and publication really execute.
    Set-Item Function:Get-EltsVerifiedDownload $realGetEltsVerifiedDownload
    # A cached file with the expected digest is reused without invoking web IO.
    $cached = Join-Path $temp 'cache.bin'; [IO.File]::WriteAllText($cached,'cache')
    $cacheHash = (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash
    $webWasCalled = $false
    function Invoke-WebRequest { $script:webWasCalled = $true; throw 'network should not be called for a matching cache' }
    $result = Get-EltsVerifiedDownload 'https://example.invalid/file' $cached $cacheHash
    Assert-True ($result -eq $cached -and -not $webWasCalled) 'matching download cache avoids network'

    $downloadBytes = 'verified fixture payload'
    $fixture = Join-Path $temp 'expected.bin'; [IO.File]::WriteAllText($fixture, $downloadBytes)
    $fixtureHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    function Invoke-WebRequest {
        param($Uri, $OutFile, [switch]$UseBasicParsing)
        $script:webWasCalled = $true
        [IO.File]::WriteAllText($OutFile, $script:downloadBytes)
    }
    $freshDownload = Join-Path $temp 'fresh.bin'
    $result = Get-EltsVerifiedDownload 'https://example.invalid/file' $freshDownload $fixtureHash
    Assert-True ($webWasCalled -and (Get-FileHash -LiteralPath $result -Algorithm SHA256).Hash -eq $fixtureHash) 'new download is hashed before publication'
    Assert-True (-not (Test-Path -LiteralPath ($freshDownload + '.partial'))) 'successful download publishes without leaving a partial'
    [IO.File]::WriteAllText($freshDownload, 'corrupt cache')
    $webWasCalled = $false
    $result = Get-EltsVerifiedDownload 'https://example.invalid/file' $freshDownload $fixtureHash
    Assert-True ($webWasCalled -and (Get-FileHash -LiteralPath $result -Algorithm SHA256).Hash -eq $fixtureHash) 'corrupt cache is downloaded and verified again'
    $script:downloadBytes = 'wrong payload'
    $badDownload = Join-Path $temp 'bad.bin'
    $badHashThrew = $false
    try { Get-EltsVerifiedDownload 'https://example.invalid/file' $badDownload $fixtureHash | Out-Null }
    catch { $badHashThrew = $_.Exception.Message -match 'checksum mismatch' }
    Assert-True ($badHashThrew -and -not (Test-Path -LiteralPath $badDownload)) 'actual hash mismatch never publishes the download'
    $insecureThrew = $false
    try { Get-EltsVerifiedDownload 'http://example.invalid/file' $badDownload $fixtureHash | Out-Null }
    catch { $insecureThrew = $_.Exception.Message -match 'HTTPS' }
    Assert-True $insecureThrew 'HTTP installer URL is rejected'
    $manifestThrew = $false
    $invalidManifest = $manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $invalidManifest.windowsSetup.pythonVersion = '../../outside'
    try { Assert-EltsSetupManifest $invalidManifest } catch { $manifestThrew = $true }
    Assert-True $manifestThrew 'path traversal in a tool version is rejected before installation'

    # A bad download must fail before the installer process can be reached.
    $downloadCalls.Clear(); $processCalls.Clear()
    function Get-EltsVerifiedDownload { throw 'Download checksum mismatch: fixture' }
    $bad = [pscustomobject]@{python=$null; dotnet='d'; node='n'; unity='u'; unityCli='c'; git='g'}
    $threw = $false
    try { Install-EltsMissingTools $bad $manifest (Join-Path $temp 'bad') } catch { $threw = $true }
    Assert-True $threw 'checksum mismatch stops installation'
    Assert-Equal $processCalls.Count 0 'checksum mismatch occurs before process execution'

    # Use a real child process for argv escaping. The
    # fixture writes each received argument, including spaces, quotes, and a
    # trailing slash, to JSON in the temporary directory.
    # Resolve the actual Windows PowerShell executable rather than assuming
    # PSHOME contains powershell.exe (portable hosts may use another PSHOME).
    $powershell = (Get-Command powershell.exe -CommandType Application).Source
    $argFixture = Join-Path $temp 'capture-args.ps1'
    [IO.File]::WriteAllText($argFixture, '[IO.File]::WriteAllText($env:ELTS_ARG_OUTPUT, ($args | ConvertTo-Json -Compress))')
    $env:ELTS_ARG_OUTPUT = Join-Path $temp 'args.json'
    $specialArguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$argFixture,'space value','quote"value','C:\trailing\')
    $argumentLine = ($specialArguments | ForEach-Object { ConvertTo-EltsProcessArgument $_ }) -join ' '
    $child = Start-Process -FilePath $powershell -ArgumentList $argumentLine -NoNewWindow -PassThru
    $child.WaitForExit()
    $received = Get-Content -Raw $env:ELTS_ARG_OUTPUT | ConvertFrom-Json
    Assert-Equal $received @('space value','quote"value','C:\trailing\') 'Windows argv escaping preserves special arguments'
    $mockProcessScript = (Get-Command Invoke-EltsSetupProcess -CommandType Function).ScriptBlock
    $exitThrew = $false; $exitMessage = ''
    Remove-Item Function:Invoke-EltsSetupProcess
    . (Join-Path $repo 'scripts/setup-tools.ps1')
    try { Invoke-EltsSetupProcess -File $powershell -Arguments @('-NoProfile','-Command','exit 17') } catch { $exitMessage = $_.Exception.Message; $exitThrew = $exitMessage -match 'exited with code 17' }
    finally { Set-Item Function:Invoke-EltsSetupProcess $mockProcessScript }
    Assert-True $exitThrew ('helper preserves exact failed-child exit code 17: ' + $exitMessage)
    Remove-Item Function:Invoke-EltsSetupProcess
    . (Join-Path $repo 'scripts/setup-tools.ps1')
    $successThrew = $false
    try { Invoke-EltsSetupProcess -File $powershell -Arguments @('-NoProfile','-Command','exit 0') }
    catch { $successThrew = $true }
    Assert-True (-not $successThrew) 'successful child exit 0 is not mistaken for failure'
    $restartThrew = $false
    try { Invoke-EltsSetupProcess -File $powershell -Arguments @('-NoProfile','-Command','exit 3010') -SuccessCodes @(0,3010) }
    catch { $restartThrew = $_.Exception.Message -match 'requires a Windows restart' }
    Assert-True $restartThrew 'installer reboot exit code is reported as requiring restart'
    Set-Item Function:Invoke-EltsSetupProcess $mockProcessScript

    # Probe the actual supplied interpreter when available, including both
    # supported-range boundaries and an obviously wrong executable.
    if ($PythonExecutable) {
        Assert-True (Test-EltsPython $PythonExecutable $manifest) 'bundled Python passes supported range probe'
        $tooOld = $manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $tooOld.python.minimumVersion = '99.0'
        Assert-True (-not (Test-EltsPython $PythonExecutable $tooOld)) 'Python below configured minimum is rejected'
        $tooNew = $manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $tooNew.python.maximumExclusiveVersion = '3.0'
        Assert-True (-not (Test-EltsPython $PythonExecutable $tooNew)) 'Python at or above configured maximum is rejected'
    }
    Assert-True (-not (Test-EltsPython $powershell $manifest)) 'wrong interpreter probe is rejected'

    # Local setup-path data is parsed as data. A command-looking JSON string is
    # never evaluated while loading it.
    $oldRoot = $script:EltsRepositoryRoot
    $isolatedRoot = Join-Path $temp 'isolated-root'; New-Item -ItemType Directory -Path (Join-Path $isolatedRoot 'config/local') -Force | Out-Null
    $marker = Join-Path $temp 'should-not-exist.txt'
    $maliciousPathData = [pscustomobject]@{python=('$(New-Item -ItemType File -Path ' + $marker + ')'); node=$null} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $isolatedRoot 'config/local/development-tools.json'), $maliciousPathData)
    $script:EltsRepositoryRoot = $isolatedRoot
    Initialize-EltsLocalTools
    Assert-True (-not (Test-Path $marker)) 'local setup-path JSON is not executed as code'
    [IO.File]::WriteAllText((Join-Path $isolatedRoot 'config/local/development-tools.json'), '{broken')
    $brokenThrew = $false
    try { Initialize-EltsLocalTools -WarningAction SilentlyContinue } catch { $brokenThrew = $true }
    Assert-True (-not $brokenThrew) 'a damaged generated tool-path cache permits setup rediscovery'
    $script:EltsRepositoryRoot = $oldRoot
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($temp)
    $allowedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTemp.StartsWith($allowedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedTemp) -notlike 'elts-setup-tests-*') {
        throw 'Refusing to remove a test directory outside its temporary root.'
    }
    Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:ELTS_ARG_OUTPUT -ErrorAction SilentlyContinue
}

if ($failures.Count) {
    $failures | ForEach-Object { Write-Host ('FAIL: ' + $_) -ForegroundColor Red }
    Write-Host ("{0} passed; {1} failed." -f $passes,$failures.Count)
    exit 1
}
Write-Host ("PASS: {0} offline setup helper regression tests." -f $passes) -ForegroundColor Green
# Some probes intentionally launch a failing executable. GitHub's PowerShell
# wrapper propagates LASTEXITCODE after this script, so explicitly return the
# suite outcome rather than the last negative-test probe's native exit code.
exit 0
