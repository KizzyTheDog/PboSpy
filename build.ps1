param([switch]$Cli, [switch]$Run, [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$project = Join-Path $root 'src\PboSpy\PboSpy.csproj'
$settingsFile = Join-Path $root 'build.settings.json'

$cfg = [pscustomobject][ordered]@{
    Output = '..\PboSpy Built'; Configuration = 'Release'; SelfContained = $false; SingleFile = $false
    Clean = $false; Desktop = $true; StartMenu = $true; ContextMenu = $false; DeleteOld = $true
    Launch = $true; OpenFolder = $false
}
if (Test-Path $settingsFile) {
    try {
        $saved = Get-Content $settingsFile -Raw | ConvertFrom-Json
        foreach ($p in $saved.PSObject.Properties) { if ($cfg.PSObject.Properties[$p.Name]) { $cfg.($p.Name) = $p.Value } }
    } catch { }
}

function Resolve-Output([string]$path) {
    if ([IO.Path]::IsPathRooted($path)) { return $path }
    return Join-Path $root $path
}

$contextExtensions = '.pbo', '.paa', '.pac', '.wss', '.bin', '.rvmat', '.p3d'

function New-Shortcut([string]$folder, [string]$exe, [string]$name = 'PboSpy', [string]$arguments = '', [string]$description = 'PBO explorer and converter for Arma 3') {
    if (-not (Test-Path $folder)) { New-Item -ItemType Directory $folder | Out-Null }
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path $folder "$name.lnk"))
    $lnk.TargetPath = $exe
    $lnk.Arguments = $arguments
    $lnk.WorkingDirectory = Split-Path $exe
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = $description
    $lnk.Save()
}

function Set-ContextMenu([string]$exe) {
    foreach ($ext in $contextExtensions) {
        $key = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\PboSpy"
        New-Item -Path "$key\command" -Force | Out-Null
        Set-ItemProperty -LiteralPath $key -Name '(default)' -Value 'Open with PboSpy'
        Set-ItemProperty -LiteralPath $key -Name 'Icon' -Value "`"$exe`",0"
        Set-ItemProperty -LiteralPath "$key\command" -Name '(default)' -Value "`"$exe`" `"%1`""
    }
}

function Remove-Integration {
    $removed = 0
    foreach ($path in @((Join-Path ([Environment]::GetFolderPath('Desktop')) 'PboSpy.lnk'),
                        (Join-Path ([Environment]::GetFolderPath('Programs')) 'PboSpy'))) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force; $removed++ }
    }
    # Shortcuts left by older builds in other folders (renamed or moved) that still point at a PboSpy.exe.
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        foreach ($lnk in Get-ChildItem $folder -Filter *.lnk -Recurse -ErrorAction SilentlyContinue) {
            try {
                if ((Split-Path $shell.CreateShortcut($lnk.FullName).TargetPath -Leaf) -eq 'PboSpy.exe') { Remove-Item $lnk.FullName -Force; $removed++ }
            } catch { }
        }
    }
    foreach ($ext in $contextExtensions) {
        $key = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\PboSpy"
        if (Test-Path $key) { Remove-Item $key -Recurse -Force; $removed++ }
    }
    return $removed
}

# BisDll.dll (ODOL -> MLOD) isn't redistributable. It's taken from the user's own P3D Debinarizer:
# either a loose BisDll.dll or the one bundled inside P3DDebin.exe.
function Find-BisDll {
    $target = Join-Path $root 'libs\BisDll\BisDll.dll'
    if (Test-Path $target) { return $true }
    $tools = Split-Path $root -Parent
    $loose = Get-ChildItem $tools -Filter 'BisDll.dll' -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($loose) {
        New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
        Copy-Item $loose.FullName $target
        return $true
    }
    foreach ($exe in Get-ChildItem $tools -Filter 'P3DDebin*.exe' -Recurse -Depth 3 -ErrorAction SilentlyContinue) {
        try {
            if (Export-BundledFile $exe.FullName 'BisDll.dll' $target) { return $true }
        } catch { }
    }
    return $false
}

function Export-BundledFile([string]$exe, [string]$name, [string]$target) {
    $bytes = [IO.File]::ReadAllBytes($exe)
    $signature = [byte[]](0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
    $position = -1
    for ($i = 0; $i -lt $bytes.Length - 32; $i++) {
        if ($bytes[$i] -ne 0x8b -or $bytes[$i + 1] -ne 0x12) { continue }
        $match = $true
        for ($k = 2; $k -lt 32; $k++) { if ($bytes[$i + $k] -ne $signature[$k]) { $match = $false; break } }
        if ($match) { $position = $i; break }
    }
    if ($position -lt 8) { return $false }
    $stream = New-Object IO.MemoryStream(,$bytes)
    $stream.Position = [BitConverter]::ToInt64($bytes, $position - 8)
    $reader = New-Object IO.BinaryReader($stream)
    $major = $reader.ReadUInt32(); [void]$reader.ReadUInt32(); $count = $reader.ReadInt32(); [void]$reader.ReadString()
    if ($major -ge 2) { 1..4 | ForEach-Object { [void]$reader.ReadInt64() }; [void]$reader.ReadUInt64() }
    for ($f = 0; $f -lt $count; $f++) {
        $offset = $reader.ReadInt64(); $size = $reader.ReadInt64(); $compressed = 0
        if ($major -ge 6) { $compressed = $reader.ReadInt64() }
        [void]$reader.ReadByte(); $path = $reader.ReadString()
        if ($path -ne $name) { continue }
        if ($compressed -gt 0) {
            $source = New-Object IO.MemoryStream($bytes, [int]$offset, [int]$compressed)
            $inflate = New-Object IO.Compression.DeflateStream($source, [IO.Compression.CompressionMode]::Decompress)
            $output = New-Object IO.MemoryStream
            $inflate.CopyTo($output)
            $data = $output.ToArray()
        } else {
            $data = New-Object byte[] $size
            [Array]::Copy($bytes, $offset, $data, 0, $size)
        }
        New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
        [IO.File]::WriteAllBytes($target, $data)
        return $true
    }
    return $false
}

function Write-Log([string]$text) {
    if ($script:logBox) { $script:logBox.AppendText($text + [Environment]::NewLine) } else { Write-Host $text }
}

# Each step returns nothing, or @(exe, args) to run as a process.
function Get-Steps($c) {
    $out = Resolve-Output $c.Output
    $steps = New-Object System.Collections.ArrayList
    [void]$steps.Add(@{ Name = 'Checking .NET SDK'; Action = {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK not found. Install it from https://dotnet.microsoft.com/download' }
    } })
    [void]$steps.Add(@{ Name = 'Checking bis-file-formats'; Action = {
        if (-not (Test-Path (Join-Path $root 'libs\bis-file-formats\BIS.PAA\BIS.PAA.csproj'))) {
            return @('git', 'clone --depth 1 https://github.com/jetelain/bis-file-formats.git libs\bis-file-formats')
        }
    } })
    [void]$steps.Add(@{ Name = 'Looking for BisDll (P3D debinarizing)'; Action = {
        if (Find-BisDll) { Write-Log '  BisDll.dll ready.' } else { Write-Log '  BisDll.dll not found: P3D Tools will only change paths. Put P3DDebin.exe or BisDll.dll next to the PboSpy folder and build again.' }
    } })
    [void]$steps.Add(@{ Name = 'Closing running PboSpy'; Action = {
        Get-Process PboSpy -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 300
    } })
    if ($c.Clean) {
        [void]$steps.Add(@{ Name = 'Cleaning'; Action = {
            if (Test-Path $out) { Remove-Item $out -Recurse -Force }
            return @('dotnet', "clean `"$project`" -c $($c.Configuration) -v q --nologo")
        }.GetNewClosure() })
    }
    $publishArgs = "publish `"$project`" -c $($c.Configuration) -r win-x64 -o `"$out`" -v q --nologo"
    $publishArgs += if ($c.SelfContained) { ' --self-contained true' } else { ' --self-contained false' }
    if ($c.SingleFile) { $publishArgs += ' -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true' }
    [void]$steps.Add(@{ Name = 'Building'; Action = { return @('dotnet', $publishArgs) }.GetNewClosure() })
    [void]$steps.Add(@{ Name = 'Finishing'; Action = {
        $exe = Join-Path $out 'PboSpy.exe'
        if (-not (Test-Path $exe)) { throw "Build finished but $exe is missing." }
        $changes = Join-Path $PSScriptRoot 'CHANGES.md'
        if (Test-Path $changes) { Copy-Item $changes (Join-Path $out 'CHANGES.md') -Force }
        if ($c.DeleteOld -and ($c.Desktop -or $c.StartMenu -or $c.ContextMenu)) {
            $n = Remove-Integration
            if ($n -gt 0) { Write-Log "Deleted $n old shortcut / menu entr$(if ($n -eq 1) { 'y' } else { 'ies' })." }
        }
        if ($c.Desktop) { New-Shortcut ([Environment]::GetFolderPath('Desktop')) $exe; Write-Log 'Desktop shortcut created.' }
        if ($c.StartMenu) {
            $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PboSpy'
            New-Shortcut $menu $exe
            New-Shortcut $menu $exe 'P3D Tools (PboSpy)' '--p3d' 'Debinarize P3D models, extract RVMATs and model.cfg'
            New-Shortcut $menu $exe 'PBR Texture Maker (PboSpy)' '--pbr' 'Turn Arma textures into PBR maps'
            Write-Log 'Start menu shortcuts created (PboSpy, P3D Tools, PBR Texture Maker).'
        }
        if ($c.ContextMenu) { Set-ContextMenu $exe; Write-Log 'Added "Open with PboSpy" to the right-click menu.' }
        Write-Log "Done: $exe"
        if ($c.OpenFolder) { Start-Process explorer.exe "`"$out`"" }
        if ($c.Launch) { Start-Process $exe }
    }.GetNewClosure() })
    return ,$steps
}

if ($Cli) {
    $cfg.Launch = [bool]$Run
    $cfg.OpenFolder = $false
    if (-not $Install) { $cfg.Desktop = $false; $cfg.StartMenu = $false; $cfg.ContextMenu = $false }
    try {
        foreach ($step in (Get-Steps $cfg)) {
            Write-Host "$($step.Name)..."
            $cmd = & $step.Action
            if ($cmd) {
                & cmd.exe /c "$($cmd[0]) $($cmd[1])"
                if ($LASTEXITCODE -ne 0) { Write-Host "$($step.Name) failed." -ForegroundColor Red; exit 1 }
            }
        }
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
    exit 0
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$bg = [Drawing.Color]::FromArgb(30, 30, 30)
$panel = [Drawing.Color]::FromArgb(45, 45, 48)
$fg = [Drawing.Color]::FromArgb(230, 230, 230)
$dim = [Drawing.Color]::FromArgb(150, 150, 150)
$accent = [Drawing.Color]::FromArgb(0, 122, 204)

$form = New-Object Windows.Forms.Form
$form.Text = 'PboSpy Builder'
$form.ClientSize = New-Object Drawing.Size(560, 600)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.BackColor = $bg
$form.ForeColor = $fg
$form.Font = New-Object Drawing.Font('Segoe UI', 9)
$tooltip = New-Object Windows.Forms.ToolTip

function Add-Control($ctl, $x, $y, $w, $h) {
    $ctl.Location = New-Object Drawing.Point($x, $y)
    if ($w) { $ctl.Size = New-Object Drawing.Size($w, $h) }
    $form.Controls.Add($ctl)
    return $ctl
}
function New-Label($text, $x, $y, [switch]$Header) {
    $l = New-Object Windows.Forms.Label
    $l.Text = $text; $l.AutoSize = $true
    if ($Header) { $l.Font = New-Object Drawing.Font('Segoe UI Semibold', 10); $l.ForeColor = $accent }
    return Add-Control $l $x $y
}
function New-Check($text, $x, $y, $checked, $tip) {
    $c = New-Object Windows.Forms.CheckBox
    $c.Text = $text; $c.AutoSize = $true; $c.Checked = [bool]$checked
    if ($tip) { $tooltip.SetToolTip($c, $tip) }
    return Add-Control $c $x $y
}
function New-Button($text, $x, $y, $w, $h, [switch]$Primary) {
    $b = New-Object Windows.Forms.Button
    $b.Text = $text; $b.FlatStyle = 'Flat'; $b.FlatAppearance.BorderColor = [Drawing.Color]::FromArgb(70, 70, 74)
    $b.BackColor = if ($Primary) { $accent } else { $panel }
    $b.ForeColor = $fg
    return Add-Control $b $x $y $w $h
}

$title = New-Label 'PboSpy Builder' 16 10
$title.Font = New-Object Drawing.Font('Segoe UI Light', 16)
$sub = New-Label 'Builds PboSpy from this folder and sets it up on this PC.' 18 46
$sub.ForeColor = $dim

New-Label 'Build' 16 80 -Header | Out-Null
New-Label 'Output folder' 18 110 | Out-Null
$outBox = Add-Control (New-Object Windows.Forms.TextBox) 120 107 330 23
$outBox.Text = $cfg.Output; $outBox.BackColor = $panel; $outBox.ForeColor = $fg; $outBox.BorderStyle = 'FixedSingle'
$browse = New-Button 'Browse...' 458 106 86 25
$browse.Add_Click({
    $dlg = New-Object Windows.Forms.FolderBrowserDialog
    $dlg.SelectedPath = Resolve-Output $outBox.Text
    if ($dlg.ShowDialog($form) -eq 'OK') { $outBox.Text = $dlg.SelectedPath }
})

New-Label 'Configuration' 18 143 | Out-Null
$confBox = Add-Control (New-Object Windows.Forms.ComboBox) 120 140 120 23
$confBox.DropDownStyle = 'DropDownList'; [void]$confBox.Items.AddRange(@('Release', 'Debug'))
$confBox.SelectedItem = $cfg.Configuration; if ($confBox.SelectedIndex -lt 0) { $confBox.SelectedIndex = 0 }
$confBox.BackColor = $panel; $confBox.ForeColor = $fg; $confBox.FlatStyle = 'Flat'

$selfBox = New-Check 'Self-contained (runs without the .NET 6 Desktop Runtime, ~150 MB)' 18 172 $cfg.SelfContained
$singleBox = New-Check 'Single .exe file' 18 196 $cfg.SingleFile 'Packs everything into PboSpy.exe. Best combined with self-contained.'
$cleanBox = New-Check 'Clean build (wipe the output folder first)' 18 220 $cfg.Clean

New-Label 'Install' 16 256 -Header | Out-Null
$deskBox = New-Check 'Desktop shortcut' 18 284 $cfg.Desktop
$startBox = New-Check 'Start menu shortcut' 200 284 $cfg.StartMenu
$oldBox = New-Check 'Delete old ones first' 380 284 $cfg.DeleteOld 'Removes shortcuts and right-click entries from earlier builds (also ones pointing at an old folder) before making new ones.' 
$ctxBox = New-Check 'Add "Open with PboSpy" to the right-click menu of .pbo .paa .wss .bin .rvmat .p3d' 18 308 $cfg.ContextMenu 'Per-user only, no admin needed. Undo with "Remove shortcuts".'

New-Label 'After build' 16 344 -Header | Out-Null
$launchBox = New-Check 'Launch PboSpy' 18 372 $cfg.Launch
$openBox = New-Check 'Open the output folder' 200 372 $cfg.OpenFolder

$logBox = Add-Control (New-Object Windows.Forms.TextBox) 16 404 528 128
$logBox.Multiline = $true; $logBox.ReadOnly = $true; $logBox.ScrollBars = 'Vertical'
$logBox.BackColor = [Drawing.Color]::FromArgb(20, 20, 20); $logBox.ForeColor = $dim; $logBox.BorderStyle = 'FixedSingle'
$logBox.Font = New-Object Drawing.Font('Consolas', 8.5)
$script:logBox = $logBox

$progress = Add-Control (New-Object Windows.Forms.ProgressBar) 16 540 528 6
$progress.Style = 'Marquee'; $progress.MarqueeAnimationSpeed = 0

$removeBtn = New-Button 'Remove shortcuts' 16 556 130 30
$tooltip.SetToolTip($removeBtn, 'Removes the desktop / start menu shortcuts and the right-click entries. Leaves the build alone.')
$closeBtn = New-Button 'Close' 336 556 96 30
$buildBtn = New-Button 'Build' 440 556 104 30 -Primary

$removeBtn.Add_Click({
    $n = Remove-Integration
    Write-Log "Removed $n shortcut / menu entr$(if ($n -eq 1) { 'y' } else { 'ies' })."
})
$closeBtn.Add_Click({ $form.Close() })

$state = @{ Steps = $null; Index = 0; Proc = $null; LogFile = $null; LogPos = 0 }
$timer = New-Object Windows.Forms.Timer
$timer.Interval = 150

function Set-Busy([bool]$busy) {
    foreach ($c in $form.Controls) { if ($c -ne $logBox -and $c -ne $progress -and $c -ne $closeBtn) { $c.Enabled = -not $busy } }
    $progress.MarqueeAnimationSpeed = if ($busy) { 30 } else { 0 }
    $closeBtn.Text = if ($busy) { 'Cancel' } else { 'Close' }
}

function Read-ProcLog {
    if (-not $state.LogFile -or -not (Test-Path $state.LogFile)) { return }
    $fs = [IO.File]::Open($state.LogFile, 'Open', 'Read', 'ReadWrite')
    try {
        $fs.Position = $state.LogPos
        $text = (New-Object IO.StreamReader($fs)).ReadToEnd()
        $state.LogPos = $fs.Position
    } finally { $fs.Dispose() }
    foreach ($line in ($text -split "`r?`n")) {
        if ($line.Trim() -and $line -notmatch 'NETSDK1138|NU1701|NETSDK1201|CS0108') { Write-Log ('  ' + $line.Trim()) }
    }
}

function Stop-Build([string]$message) {
    $timer.Stop()
    $state.Proc = $null
    Set-Busy $false
    if ($message) { Write-Log $message }
}

$timer.Add_Tick({
    try {
        if ($state.Proc) {
            Read-ProcLog
            if (-not $state.Proc.HasExited) { return }
            Read-ProcLog
            $code = $state.Proc.ExitCode
            Remove-Item $state.LogFile -ErrorAction SilentlyContinue
            $state.Proc = $null
            if ($code -ne 0) {
                Stop-Build "FAILED (exit code $code)."
                [Windows.Forms.MessageBox]::Show($form, 'Build failed, see the log.', 'PboSpy Builder', 'OK', 'Error') | Out-Null
                return
            }
            $state.Index++
        }
        if ($state.Index -ge $state.Steps.Count) { Stop-Build ''; return }
        $step = $state.Steps[$state.Index]
        Write-Log "$($step.Name)..."
        $cmd = & $step.Action
        if ($cmd) {
            $state.LogFile = [IO.Path]::GetTempFileName(); $state.LogPos = 0
            $psi = New-Object Diagnostics.ProcessStartInfo('cmd.exe', "/c $($cmd[0]) $($cmd[1]) > `"$($state.LogFile)`" 2>&1")
            $psi.CreateNoWindow = $true; $psi.UseShellExecute = $false; $psi.WorkingDirectory = $root
            $state.Proc = [Diagnostics.Process]::Start($psi)
        } else {
            $state.Index++
        }
    } catch {
        Stop-Build "FAILED: $($_.Exception.Message)"
    }
})

$buildBtn.Add_Click({
    $c = [pscustomobject][ordered]@{
        Output = $outBox.Text.Trim(); Configuration = [string]$confBox.SelectedItem
        SelfContained = $selfBox.Checked; SingleFile = $singleBox.Checked; Clean = $cleanBox.Checked
        Desktop = $deskBox.Checked; StartMenu = $startBox.Checked; ContextMenu = $ctxBox.Checked; DeleteOld = $oldBox.Checked
        Launch = $launchBox.Checked; OpenFolder = $openBox.Checked
    }
    if (-not $c.Output) { $c.Output = '..\PboSpy Built'; $outBox.Text = $c.Output }
    try { $c | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8 } catch { }
    $logBox.Clear()
    $state.Steps = Get-Steps $c
    $state.Index = 0
    Set-Busy $true
    $timer.Start()
})

$form.Add_FormClosing({
    param($s, $e)
    if ($state.Proc -and -not $state.Proc.HasExited) {
        if ([Windows.Forms.MessageBox]::Show($form, 'Stop the build?', 'PboSpy Builder', 'YesNo', 'Question') -ne 'Yes') { $e.Cancel = $true; return }
        & taskkill /pid $state.Proc.Id /t /f | Out-Null
    }
    $timer.Stop()
})

$form.AcceptButton = $buildBtn
[void]$form.ShowDialog()
