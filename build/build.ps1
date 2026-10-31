<#
.SYNOPSIS
  Builds Format2Format on Windows: runs the tests, publishes the app and compiles the installer.

.DESCRIPTION
  Requires the .NET 8 SDK and Inno Setup 6.3+ (https://jrsoftware.org/isdl.php).
  Output: publish\win-x64\ (the app) and dist\Format2Format-<version>-setup.exe (the installer).
#>
param([switch] $SkipTests, [switch] $SkipInstaller)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

if (-not $SkipTests) {
    dotnet test -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}

dotnet publish src/Format2Format -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if (-not $iscc) { throw 'Inno Setup (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php or use -SkipInstaller.' }
    & $iscc installer\Format2Format.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    Get-ChildItem dist\*.exe | ForEach-Object { Write-Host "Installer: $($_.FullName)" }
}
