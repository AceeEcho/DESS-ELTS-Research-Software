# Exercise the real process/log readiness checks without launching Unity.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'scripts/setup-tools.ps1')
. (Join-Path $repo 'scripts/start-dev-tools.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ELTS player start ' + [Guid]::NewGuid().ToString('N'))
$shell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }
try {
    New-Item -ItemType Directory $fixture | Out-Null
    $child = Join-Path $fixture 'fixture.ps1'
    @'
param($LogPath, $Scenario)
if ($Scenario -eq 'ready') { Set-Content -LiteralPath $LogPath 'ELTS_ADMINISTRATOR_READY http://127.0.0.1:18764/#fixture' }
if ($Scenario -eq 'failure') { Set-Content -LiteralPath $LogPath 'ELTS_VIEW_INIT_FAIL System.ArgumentException: Development game settings are outside their safe synthetic ranges.' }
if ($Scenario -ne 'exit') { Start-Sleep -Seconds 3 }
exit 7
'@ | Set-Content -LiteralPath $child
    # A previous player's success must not satisfy a silent new startup.
    Set-Content (Join-Path $fixture 'old-player.log') 'ELTS_ADMINISTRATOR_READY http://127.0.0.1:18764/#old'
    foreach ($scenario in @('ready','failure','exit','silent')) {
        $log = Join-Path $fixture ($scenario + '.log')
        $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$child,$log,$scenario)
        $line = ($arguments | ForEach-Object { ConvertTo-EltsProcessArgument $_ }) -join ' '
        $process = Start-Process $shell -ArgumentList $line -WindowStyle Hidden -PassThru
        $null = $process.Handle
        try {
            $message = ''
            $timeout = if ($scenario -eq 'silent') { 2 } else { 15 }
            try { Wait-EltsPlayerReady $process $log -TimeoutSeconds $timeout }
            catch { $message = $_.Exception.Message }
            switch ($scenario) {
                'ready' { Assert (-not $message) "Ready player rejected: $message" }
                'failure' { Assert ($message -match 'outside their safe synthetic ranges' -and $message.Contains($log)) 'Scene failure must include its cause and log path' }
                'exit' { Assert ($message -match 'exit code 7' -and $message.Contains($log)) 'An exited player must not count as a successful launch' }
                'silent' {
                    Assert ($message -match 'did not initialize' -and $message.Contains($log)) 'A running process or stale log must not count as ready'
                    Assert (-not $process.HasExited) 'A startup timeout must leave the process running'
                }
            }
        } finally {
            # Fixture children exit on their own; do not terminate another process.
            $process.WaitForExit()
            $process.Dispose()
        }
    }
    Write-Host 'PASS: player readiness, scene initialization failure, early exit, and timeout with stale-log isolation.'
} finally {
    if ([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
