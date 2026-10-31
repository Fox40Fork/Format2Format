<#
.SYNOPSIS
  Quick end-to-end checks that need real Windows (used by CI; can also be run locally).

.EXAMPLE
  ./build/smoke-tests.ps1 -Test Registration -Exe publish/win-x64/Format2Format.exe
  ./build/smoke-tests.ps1 -Test SingleInstance -Exe publish/win-x64/Format2Format.exe
  ./build/smoke-tests.ps1 -Test Installer -Setup dist/Format2Format-1.0.0-setup.exe
#>
param(
    [Parameter(Mandatory)] [ValidateSet('Registration', 'SingleInstance', 'Installer')] [string] $Test,
    [string] $Exe,
    [string] $Setup
)

$ErrorActionPreference = 'Stop'
$extensions = '.jpg', '.jpeg', '.jfif', '.png', '.webp', '.bmp', '.gif', '.tif', '.tiff', '.ico', '.heic', '.heif', '.avif'
$logFile = Join-Path $env:LOCALAPPDATA 'Format2Format\log.txt'

function VerbKey([string] $ext) { "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\Format2Format" }

function Run([string] $file, [string[]] $arguments) {
    $p = Start-Process -FilePath $file -ArgumentList $arguments -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "$file $arguments exited with $($p.ExitCode)" }
}

function Assert-Registered([string] $exePath) {
    foreach ($ext in $extensions) {
        $command = (Get-ItemProperty -Path "$(VerbKey $ext)\command").'(default)'
        if ($command -ne "`"$exePath`" `"%1`"") { throw "Unexpected command for ${ext}: $command" }
        $model = (Get-ItemProperty -Path (VerbKey $ext)).MultiSelectModel
        if ($model -ne 'Player') { throw "MultiSelectModel missing for $ext" }
    }
    Write-Host "Right-click entry registered for $($extensions.Count) extensions -> $exePath"
}

function Assert-NotRegistered {
    foreach ($ext in $extensions) {
        if (Test-Path (VerbKey $ext)) { throw "Right-click entry for $ext still present" }
    }
    Write-Host 'Right-click entry not present'
}

function Wait-Until([scriptblock] $condition, [int] $seconds, [string] $what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while (-not (& $condition)) {
        if ((Get-Date) -gt $deadline) { throw "Timed out waiting for: $what" }
        Start-Sleep -Milliseconds 250
    }
}

switch ($Test) {
    'Registration' {
        $exePath = (Resolve-Path $Exe).Path
        Run $exePath '--register-shell'
        Assert-Registered $exePath
        Run $exePath '--unregister-shell'
        Assert-NotRegistered
    }

    'SingleInstance' {
        # Explorer starts one process per selected file; they must all end up in one window.
        $exePath = (Resolve-Path $Exe).Path
        Add-Type -AssemblyName System.Drawing
        $dir = Join-Path $env:RUNNER_TEMP 'f2f-images'
        if (-not $env:RUNNER_TEMP) { $dir = Join-Path ([IO.Path]::GetTempPath()) 'f2f-images' }
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $images = foreach ($name in 'one', 'two', 'three') {
            $bmp = [Drawing.Bitmap]::new(20, 20)
            $path = Join-Path $dir "$name.png"
            $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            $path
        }
        Remove-Item $logFile -ErrorAction SilentlyContinue

        $primary = Start-Process -FilePath $exePath -ArgumentList "`"$($images[0])`"" -PassThru
        try {
            Wait-Until { $primary.Refresh(); $primary.MainWindowHandle -ne 0 } 60 'main window'
            foreach ($image in $images[1..2]) {
                $secondary = Start-Process -FilePath $exePath -ArgumentList "`"$image`"" -PassThru
                if (-not $secondary.WaitForExit(30000)) { throw 'Second process did not exit (no single-instance hand-off)' }
                if ($secondary.ExitCode -ne 0) { throw "Second process exited with $($secondary.ExitCode)" }
            }

            Wait-Until { (Select-String -Path $logFile -Pattern 'Added 1 file\(s\) to the list' -SimpleMatch:$false).Count -ge 3 } 30 'all files added to the first window'
            $handed = (Select-String -Path $logFile -Pattern 'Handed 1 file(s) to the running window' -SimpleMatch).Count
            if ($handed -ne 2) { throw "Expected 2 hand-offs, saw $handed" }
            if ($primary.HasExited) { throw 'The first window closed unexpectedly' }
            Write-Host 'Three processes, one window: OK'
        }
        finally {
            if (-not $primary.HasExited) { $primary.Kill() }
            Get-Content $logFile -ErrorAction SilentlyContinue | Write-Host
        }
    }

    'Installer' {
        $installDir = Join-Path $env:LOCALAPPDATA 'Programs\Format2Format'
        $installedExe = Join-Path $installDir 'Format2Format.exe'

        Run (Resolve-Path $Setup).Path '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL'
        if (-not (Test-Path $installedExe)) { throw "Not installed to $installDir" }
        Assert-Registered $installedExe

        Run (Join-Path $installDir 'unins000.exe') '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
        # The uninstaller hands off to a copy of itself in %TEMP%, so wait for the folder to go away.
        Wait-Until { -not (Test-Path $installedExe) } 120 'uninstall to finish'
        Assert-NotRegistered
        Write-Host 'Install + uninstall: OK'
    }
}
