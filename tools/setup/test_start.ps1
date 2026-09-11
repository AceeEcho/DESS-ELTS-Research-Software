# Offline orchestration tests: no installers, editors, or real players launched.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'scripts/setup-tools.ps1')
. (Join-Path $repo 'scripts/start-dev-tools.ps1')
$originalLocation = Get-Location
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ELTS startup tests ' + [Guid]::NewGuid().ToString('N'))
$script:EltsRepositoryRoot = $fixture
$script:calls = New-Object 'System.Collections.Generic.List[string]'
$script:open = $false
$script:failSetup = $false
$script:failBuild = $false
$script:fingerprint = 'source-one'
function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Get-EltsSetupPaths { return @{git='git';python='python';dotnet='dotnet';node='node';unity='Unity.exe'} }
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
        $script:calls.Add('dashboard')
    }
}
function Invoke-EltsSetupProcess {
    param($File, $Arguments, $TimeoutSeconds)
    if (($Arguments -join ' ') -match 'setup-dev.ps1') {
        $script:calls.Add('setup')
        if ($script:failSetup) { throw 'fixture setup failed' }
        @{result='ready';toolchainSha256=(Get-FileHash (Join-Path $fixture 'config/toolchain.json')).Hash} |
            ConvertTo-Json | Set-Content (Join-Path $fixture 'diagnostics/setup-report.json')
    } elseif (($Arguments -join ' ') -match 'build.ps1') {
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
    $calls.Clear()
    Invoke-EltsDevelopmentStart
    Assert (($calls -join ',') -eq 'verify,dashboard') 'Repeat start must reuse the build without opening the Editor'
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
    $calls.Clear(); $script:fingerprint = 'source-three'; $script:failBuild = $true
    try { Invoke-EltsDevelopmentStart } catch { Assert ($_.Exception.Message -eq 'fixture build failed') 'Wrong build failure' }
    Assert (($calls -join ',') -eq 'build') 'Apps opened after failed build'
    $calls.Clear(); $script:failSetup = $true
    Set-Content (Join-Path $fixture 'diagnostics/setup-report.json') '{}'
    try { Invoke-EltsDevelopmentStart } catch { Assert ($_.Exception.Message -eq 'fixture setup failed') 'Wrong setup failure' }
    Assert (($calls -join ',') -eq 'setup') 'Apps opened after failed setup'
    Write-Host 'PASS: 7 startup scenarios, including paths with spaces and failure propagation.'
} finally {
    Set-Location $originalLocation
    # The only recursive deletion is the exact temporary fixture created above.
    if ([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
