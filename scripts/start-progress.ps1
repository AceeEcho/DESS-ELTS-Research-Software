# A separate UI thread keeps the startup window responsive while setup/build waits.
# Status is informational: it never changes startup decisions or authorizes hardware.
$script:EltsStartupWindow = $null

function Start-EltsStartupWindow {
    param([string]$LogPath, [int]$RefreshMilliseconds = 200, [int]$SuccessCloseSeconds = 3)
    $state = [hashtable]::Synchronized(@{
        Status = @{ Title = 'Starting ELTS'; Detail = 'Checking this computer...'; Result = 'working' }
        StartedAt = [DateTime]::UtcNow
        FinishedAt = $null
        LogPath = $LogPath
        RefreshMilliseconds = $RefreshMilliseconds
        SuccessCloseSeconds = $SuccessCloseSeconds
        Closed = $false
        Error = $null
    })
    $runspace = [runspacefactory]::CreateRunspace()
    $runspace.ApartmentState = 'STA'
    $runspace.ThreadOptions = 'ReuseThread'
    $runspace.Open()
    $pipeline = [powershell]::Create()
    $pipeline.Runspace = $runspace
    $null = $pipeline.AddScript({
        param($state)
        $ErrorActionPreference = 'Stop'
        try {
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            [Windows.Forms.Application]::EnableVisualStyles()
            $form = New-Object Windows.Forms.Form
            $form.SuspendLayout()
            $form.Text = 'ELTS startup'
            $form.ClientSize = New-Object Drawing.Size(560, 330)
            $form.StartPosition = 'CenterScreen'
            $form.FormBorderStyle = 'FixedDialog'
            $form.MaximizeBox = $false
            $form.BackColor = [Drawing.Color]::White
            $form.Font = New-Object Drawing.Font('Segoe UI', 10)
            $form.AutoScaleMode = 'None'

            $heading = New-Object Windows.Forms.Label
            $heading.SetBounds(24, 22, 512, 36)
            $heading.Font = New-Object Drawing.Font('Segoe UI', 18, ([Drawing.FontStyle]::Bold))
            $heading.ForeColor = [Drawing.Color]::FromArgb(190, 20, 35)
            $detail = New-Object Windows.Forms.TextBox
            $detail.SetBounds(24, 70, 512, 100)
            $detail.Multiline = $true
            $detail.ReadOnly = $true
            $detail.BorderStyle = 'None'
            $detail.BackColor = [Drawing.Color]::White
            $detail.ScrollBars = 'Vertical'
            $bar = New-Object Windows.Forms.ProgressBar
            $bar.SetBounds(24, 186, 512, 12)
            $bar.Style = 'Marquee'
            $bar.MarqueeAnimationSpeed = 30
            $elapsed = New-Object Windows.Forms.Label
            $elapsed.SetBounds(24, 210, 512, 24)
            $hint = New-Object Windows.Forms.Label
            $hint.SetBounds(24, 240, 512, 24)
            $hint.Text = 'Closing this window hides progress; startup continues.'
            $hint.ForeColor = [Drawing.Color]::DimGray
            $logButton = New-Object Windows.Forms.Button
            $logButton.SetBounds(24, 280, 140, 30)
            $logButton.Text = 'Open startup log'
            $logButton.Add_Click({
                if (Test-Path -LiteralPath $state.LogPath) {
                    # Notepad is an explicitly requested interactive window.
                    Start-Process notepad.exe -ArgumentList ('"' + $state.LogPath + '"')
                }
            })
            $closeButton = New-Object Windows.Forms.Button
            $closeButton.SetBounds(426, 280, 110, 30)
            $closeButton.Text = 'Hide'
            $closeButton.Add_Click({ $form.Close() })
            $form.Controls.AddRange(@($heading, $detail, $bar, $elapsed, $hint, $logButton, $closeButton))
            # PowerShell hosts have different DPI-awareness defaults. Scale the
            # 96-DPI geometry once using this window's actual drawing context;
            # explicit fonts avoid inherited-font scaling a second time.
            $graphics = $form.CreateGraphics()
            $scale = $graphics.DpiX / 96.0
            $graphics.Dispose()
            foreach ($control in $form.Controls) {
                if ($control -ne $heading) { $control.Font = $form.Font }
                $control.SetBounds([int]($control.Left * $scale), [int]($control.Top * $scale),
                    [int]($control.Width * $scale), [int]($control.Height * $scale))
            }
            $form.ClientSize = New-Object Drawing.Size([int](560 * $scale), [int](330 * $scale))
            $form.ResumeLayout($true)
            $timer = New-Object Windows.Forms.Timer
            $timer.Interval = $state.RefreshMilliseconds
            $timer.Add_Tick({
                # Read one immutable snapshot so the title/detail always match.
                $status = $state.Status
                $heading.Text = $status.Title
                if ($detail.Text -ne $status.Detail) { $detail.Text = $status.Detail }
                $end = if ($state.FinishedAt) { $state.FinishedAt } else { [DateTime]::UtcNow }
                $duration = $end - $state.StartedAt
                $elapsed.Text = 'Elapsed: {0:00}:{1:00}  |  {2}' -f [int][Math]::Floor($duration.TotalMinutes), $duration.Seconds, $(if ($status.Result -eq 'working') { 'Waiting for this step to finish' } else { 'Startup finished' })
                $logButton.Enabled = Test-Path -LiteralPath $state.LogPath
                if ($status.Result -ne 'working') {
                    $bar.Style = 'Continuous'
                    $bar.Value = if ($status.Result -eq 'success') { 100 } else { 0 }
                    $closeButton.Text = 'Close'
                    $hint.Text = if ($status.Result -eq 'success') { 'The participant window is opening.' } else { 'Read the details above or open the startup log.' }
                    if ($status.Result -eq 'success' -and $state.FinishedAt -and
                        ([DateTime]::UtcNow - $state.FinishedAt).TotalSeconds -ge $state.SuccessCloseSeconds) {
                        $form.Close()
                    }
                }
            })
            $timer.Start()
            $null = $form.ShowDialog()
        } catch { $state.Error = $_.Exception.Message }
        finally {
            if ($timer) { $timer.Dispose() }
            if ($form) { $form.Dispose() }
            $state.Closed = $true
        }
    }).AddArgument($state)
    $handle = $pipeline.BeginInvoke()
    $script:EltsStartupWindow = @{ State = $state; Pipeline = $pipeline; Runspace = $runspace; Handle = $handle }
}

function Set-EltsStartupStatus {
    param([string]$Title, [string]$Detail, [ValidateSet('working','success','failure')][string]$Result = 'working')
    Write-Host ("${Title}: $Detail")
    if ($script:EltsStartupWindow) {
        $state = $script:EltsStartupWindow.State
        if ($Result -ne 'working') { $state.FinishedAt = [DateTime]::UtcNow }
        $state.Status = @{ Title = $Title; Detail = $Detail; Result = $Result }
    }
}

function Close-EltsStartupWindow {
    if (-not $script:EltsStartupWindow) { return }
    $window = $script:EltsStartupWindow
    try {
        # Success closes automatically; errors remain readable until dismissed.
        $null = $window.Pipeline.EndInvoke($window.Handle)
        if ($window.State.Error) { Write-Warning ('Startup window unavailable: ' + $window.State.Error) }
    } finally {
        $window.Pipeline.Dispose()
        $window.Runspace.Dispose()
        $script:EltsStartupWindow = $null
    }
}
