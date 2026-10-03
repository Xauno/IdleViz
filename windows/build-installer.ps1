<#
.SYNOPSIS
Builds IdleViz for Windows and its setup file.

.DESCRIPTION
Publishes the Release app (self-contained, x64) to artifacts\publish and compiles
artifacts\IdleViz-Setup.exe with Inno Setup. With -Install it then runs the setup file
without questions, which replaces an installed copy (stopping it first) and starts the new one.

.EXAMPLE
.\build-installer.ps1 -Install
#>
[CmdletBinding()]
param(
    # Run the setup file after building it, and start the app.
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$setupFile = Join-Path $root 'artifacts\IdleViz-Setup.exe'

function Find-InnoSetup {
    $onPath = Get-Command iscc -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup'
}

$iscc = Find-InnoSetup

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = "$($props.Project.PropertyGroup.Version)".Trim()

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Publishing IdleViz $version..."
dotnet publish (Join-Path $root 'IdleViz.App\IdleViz.App.csproj') --configuration Release --output $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
# The compiled XAML. Publish has left it out before, and the app then starts but can't open any window.
if (-not (Test-Path (Join-Path $publishDir 'IdleViz.pri'))) { throw 'The published app has no IdleViz.pri.' }

Write-Host 'Building the setup file...'
& $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $root 'installer\IdleViz.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

Write-Host "Setup file: $setupFile"

if ($Install) {
    Write-Host 'Installing...'
    # Not Start-Process -Wait: that also waits for everything the setup file starts.
    $setup = Start-Process $setupFile -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
    $setup.WaitForExit()
    if ($setup.ExitCode -ne 0) { throw "The setup file failed with exit code $($setup.ExitCode)." }
    # A silent install skips the "Start IdleViz" step, so start it here. An update goes into the
    # folder chosen at the first install, which setup records with the uninstaller.
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F0B2C1E-7C0B-4E0B-9B43-1D2A6B7C9E55}_is1'
    $installDir = (Get-ItemProperty $uninstallKey).InstallLocation
    Start-Process (Join-Path $installDir 'IdleViz.exe')
    Write-Host 'IdleViz is installed and running.'
}
