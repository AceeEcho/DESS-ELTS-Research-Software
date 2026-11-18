# Launch the portable development package with one durable rehearsal directory.
[CmdletBinding()]
param(
    [switch]$PrepareExample,
    [switch]$ChooseParticipantId,
    [ValidateRange(0, 999999)][int]$ExampleId = 0
)
$ErrorActionPreference = 'Stop'
if (-not $env:ELTS_STATION_HOME) {
    $env:ELTS_STATION_HOME = Join-Path $env:LOCALAPPDATA 'ELTS/DevelopmentRehearsal'
} elseif ($env:ELTS_STATION_HOME -notmatch '^(?:[a-zA-Z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+)') {
    throw 'ELTS_STATION_HOME must be an absolute directory.'
}
$player = Join-Path $PSScriptRoot 'player/ELTS-Synthetic.exe'
if (-not (Test-Path -LiteralPath $player -PathType Leaf)) {
    throw 'Extract the entire package before opening the rehearsal.'
}
function Select-RehearsalId([string]$Profiles) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $used = @{}
    if ([IO.Directory]::Exists($Profiles)) {
        foreach ($file in [IO.Directory]::EnumerateFiles($Profiles, 'participant-*.json')) {
            if ([IO.Path]::GetFileName($file) -match '^participant-(0|[1-9][0-9]{0,5})\.json$') {
                $used[[int]$Matches[1]] = $true
            }
        }
    }
    $next = 0
    while ($used.ContainsKey($next)) { $next++ }
    if ($next -gt 999999) { throw 'Every participant ID is already in use.' }

    $form = [Windows.Forms.Form]::new()
    try {
        $form.Text = 'Add a fictional rehearsal ID'
        $form.StartPosition = 'CenterScreen'
        $form.FormBorderStyle = 'FixedDialog'
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.ClientSize = [Drawing.Size]::new(330, 130)
        $label = [Windows.Forms.Label]::new()
        $label.SetBounds(16, 14, 300, 20)
        $label.Text = 'Choose a fictional participant ID (0 to 999999).'
        $number = [Windows.Forms.NumericUpDown]::new()
        $number.SetBounds(18, 40, 160, 28)
        $number.Maximum = 999999
        $number.Value = $next
        $number.AccessibleName = 'Fictional participant ID'
        $add = [Windows.Forms.Button]::new()
        $add.SetBounds(122, 86, 100, 30)
        $add.Text = 'Add ID'
        $add.add_Click({
            if ($used.ContainsKey([int]$number.Value)) {
                $label.Text = 'That ID is already saved. Choose another number.'
            } else { $form.DialogResult = 'OK' }
        })
        $cancel = [Windows.Forms.Button]::new()
        $cancel.SetBounds(230, 86, 82, 30)
        $cancel.Text = 'Cancel'
        $cancel.DialogResult = 'Cancel'
        $form.Controls.AddRange(@($label, $number, $add, $cancel))
        $form.AcceptButton = $add
        $form.CancelButton = $cancel
        $form.ActiveControl = $number
        if ($form.ShowDialog() -eq 'OK') { return [int]$number.Value }
        return $null
    } finally { $form.Dispose() }
}

if ($PrepareExample -or $ChooseParticipantId) {
    $profiles = Join-Path $env:ELTS_STATION_HOME 'Calibration Environment/profiles'
    if ($ChooseParticipantId) {
        $selection = Select-RehearsalId $profiles
        if ($null -eq $selection) { return }
        $ExampleId = $selection
    }
    [IO.Directory]::CreateDirectory($profiles) | Out-Null
    $destination = Join-Path $profiles "participant-$ExampleId.json"
    # Do not interleave explicit sample preparation with a station reset or copy.
    $lease = [IO.File]::Open((Join-Path $profiles '.dataset-generation'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        if (-not (Test-Path -LiteralPath $destination)) {
            $pending = Join-Path $profiles ('example-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            try {
                $example = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'samples/participant-0.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $example.participantId = $ExampleId
                [IO.File]::WriteAllText($pending, ($example | ConvertTo-Json -Depth 24), [Text.UTF8Encoding]::new($false))
                [IO.File]::Move($pending, $destination)
            } finally {
                if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) }
            }
        }
    } finally { $lease.Dispose() }
}
# The add-ID dialog can be used while the existing player is open.
if (-not $ChooseParticipantId) {
    Start-Process -FilePath $player -WorkingDirectory (Split-Path -Parent $player)
}
