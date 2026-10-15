# Windows desktop smoke test. Uses only our progress window; no app/build is started.
[CmdletBinding()]
param([string]$ScreenshotDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'scripts/start-progress.ps1')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class EltsProgressCapture {
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
'@

function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Wait-For($Check, $Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if (& $Check) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $Message
}
function Find-ProgressWindow {
    $condition = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $PID)),
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'ELTS startup')))
    [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
}
function Find-Control($Window, [string]$Name) {
    $Window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name)))
}
function Save-WindowScreenshot($Window, [string]$Name) {
    if (-not $ScreenshotDirectory) { return }
    New-Item -ItemType Directory -Force $ScreenshotDirectory | Out-Null
    $rectangle = $Window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap([int]$rectangle.Width, [int]$rectangle.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        # Capture only our own window, even if another desktop window covers it.
        $context = $graphics.GetHdc()
        try {
            Assert ([EltsProgressCapture]::PrintWindow([IntPtr]$Window.Current.NativeWindowHandle, $context, 2)) 'Window capture failed'
        } finally { $graphics.ReleaseHdc($context) }
        $bitmap.Save((Join-Path $ScreenshotDirectory $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    Start-EltsStartupWindow -LogPath $PSCommandPath -SuccessCloseSeconds 1
    Wait-For { Find-ProgressWindow } 'Progress window did not appear'
    $window = Find-ProgressWindow
    Set-EltsStartupStatus 'Building ELTS' 'Unity is building the dashboard and participant game. This can take several minutes. The console shows build output; leave startup running.'
    # The caller is blocked, as during a native build, but the UI must keep ticking.
    Start-Sleep -Seconds 2
    Assert ($null -ne (Find-Control $window 'Building ELTS')) 'UI did not update while caller was blocked'
    Save-WindowScreenshot $window 'startup-building.png'
    Set-EltsStartupStatus 'ELTS initialized' 'The participant view and administrator server are ready. The dashboard opens in your browser after the intro.' -Result success
    Wait-For { $script:EltsStartupWindow.State.Closed } 'Success did not close automatically'
    Assert (-not $script:EltsStartupWindow.State.Error) 'UI thread failed'
    Close-EltsStartupWindow

    Start-EltsStartupWindow -LogPath $PSCommandPath -SuccessCloseSeconds 1
    Wait-For { Find-ProgressWindow } 'Failure test window did not appear'
    $window = Find-ProgressWindow
    Set-EltsStartupStatus 'ELTS could not start' "Step: Build dashboard and participant game`r`nFixture build failed.`r`nHow to resolve: Close this project in Unity, then run START-ELTS.cmd." -Result failure
    Start-Sleep -Seconds 2
    Assert (-not $script:EltsStartupWindow.State.Closed) 'Failure closed before it could be read'
    Assert ($null -ne (Find-Control $window 'ELTS could not start')) 'Failure heading missing'
    Save-WindowScreenshot $window 'startup-failure.png'
    (Find-Control $window 'Close').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-For { $script:EltsStartupWindow.State.Closed } 'Close button did not dismiss failure'
    Assert (-not $script:EltsStartupWindow.State.Error) 'Failure UI thread failed'
    Close-EltsStartupWindow

    Start-EltsStartupWindow -LogPath $PSCommandPath
    Wait-For { Find-ProgressWindow } 'Hide test window did not appear'
    (Find-Control (Find-ProgressWindow) 'Hide').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-For { $script:EltsStartupWindow.State.Closed } 'Hide did not dismiss window'
    Set-EltsStartupStatus 'Opening ELTS' 'Caller continued after hiding progress.'
    Assert (-not $script:EltsStartupWindow.State.Error) 'Hidden UI thread failed'
    Close-EltsStartupWindow
    Write-Host 'PASS: progress stays responsive, success closes, failure stays readable, and hiding allows startup to continue.'
} finally {
    # Bound cleanup even when an assertion fails; never leave a test UI running.
    if ($script:EltsStartupWindow) {
        Set-EltsStartupStatus 'Test finished' 'Closing test progress window.' -Result success
        Close-EltsStartupWindow
    }
}
