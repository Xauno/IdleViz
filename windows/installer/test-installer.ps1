<#
.SYNOPSIS
Installs, updates and uninstalls IdleViz with the setup file and checks what each run leaves behind.

.DESCRIPTION
Runs artifacts\IdleViz-Setup.exe without questions, four times: a first install into a folder of
its own, an update after "Run at startup" was switched off, an update with other options, and the
uninstaller. After each it checks the files, the shortcuts and the registry.

It is meant for CI. It refuses to run on a PC that has IdleViz installed, because it would replace
and then remove that copy.
#>
[CmdletBinding()]
param(
    [string]$SetupFile = (Join-Path $PSScriptRoot '..\artifacts\IdleViz-Setup.exe')
)

$ErrorActionPreference = 'Stop'

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F0B2C1E-7C0B-4E0B-9B43-1D2A6B7C9E55}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$protocolKey = 'HKCU:\Software\Classes\idleviz'
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'IdleViz.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'IdleViz.lnk'
$installDir = Join-Path ([IO.Path]::GetTempPath()) "IdleViz test $([Guid]::NewGuid().ToString('N'))"
$exe = Join-Path $installDir 'IdleViz.exe'

if (Test-Path $uninstallKey) { throw 'IdleViz is installed on this PC. This test would replace and remove it.' }
if (-not (Test-Path $SetupFile)) { throw "No setup file at $SetupFile. Run build-installer.ps1 first." }

function Invoke-Setup([string[]]$Arguments) {
    # Not Start-Process -Wait: that also waits for everything the setup file starts.
    $setup = Start-Process $SetupFile -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') + $Arguments) -PassThru
    $setup.WaitForExit()
    if ($setup.ExitCode -ne 0) { throw "The setup file failed with exit code $($setup.ExitCode)." }
}

function Get-StartupCommand {
    (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).IdleViz
}

function Assert-That([bool]$Condition, [string]$What) {
    if (-not $Condition) { throw "Failed: $What" }
    Write-Host "  ok: $What"
}

Write-Host 'First install, default options, into a chosen folder'
Invoke-Setup @("/DIR=`"$installDir`"")
Assert-That (Test-Path $exe) 'the app is in the chosen folder'
Assert-That (Test-Path (Join-Path $installDir 'IdleViz.pri')) 'the compiled XAML is there'
Assert-That (Test-Path $startMenuShortcut) 'there is a Start menu entry'
Assert-That (-not (Test-Path $desktopShortcut)) 'there is no desktop shortcut'
Assert-That ((Get-StartupCommand) -eq "`"$exe`"") 'it runs at startup'
Assert-That ((Get-ItemProperty "$protocolKey\shell\open\command").'(default)' -eq "`"$exe`" `"%1`"") 'idleviz:// starts the app'
Assert-That ((Get-ItemProperty $uninstallKey).InstallLocation.TrimEnd('\') -eq $installDir) 'the folder is recorded'

Write-Host 'Update after Run at startup was switched off in the app'
Remove-ItemProperty $runKey -Name IdleViz
Set-Content (Join-Path $installDir 'left-over.txt') 'from an older version'
Invoke-Setup @()
Assert-That (Test-Path $exe) 'the update went into the same folder'
Assert-That (-not (Test-Path (Join-Path $installDir 'left-over.txt'))) 'a file the new version does not ship is gone'
Assert-That ($null -eq (Get-StartupCommand)) 'it still does not run at startup'
Assert-That (Test-Path $startMenuShortcut) 'the Start menu entry is kept'

Write-Host 'Update with only the desktop shortcut ticked'
Invoke-Setup @('/TASKS="desktopicon"')
Assert-That (Test-Path $desktopShortcut) 'there is a desktop shortcut'
Assert-That (-not (Test-Path $startMenuShortcut)) 'the Start menu entry is gone'
Assert-That ($null -eq (Get-StartupCommand)) 'it does not run at startup'

Write-Host 'Update with Run at startup ticked again'
Invoke-Setup @('/TASKS="startmenu,runatstartup"')
Assert-That ((Get-StartupCommand) -eq "`"$exe`"") 'it runs at startup'
Assert-That (Test-Path $startMenuShortcut) 'the Start menu entry is back'
Assert-That (-not (Test-Path $desktopShortcut)) 'the desktop shortcut is gone'

Write-Host 'Update that changes nothing'
Invoke-Setup @()
Assert-That ((Get-StartupCommand) -eq "`"$exe`"") 'it still runs at startup'

Write-Host 'Uninstall'
$uninstaller = Start-Process (Join-Path $installDir 'unins000.exe') -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$uninstaller.WaitForExit()
# The uninstaller hands over to a copy of itself in the temp folder, which finishes a moment later.
$deadline = (Get-Date).AddSeconds(60)
while ((Test-Path $installDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Assert-That (-not (Test-Path $installDir)) 'the folder is gone'
Assert-That (-not (Test-Path $startMenuShortcut)) 'the Start menu entry is gone'
Assert-That (-not (Test-Path $protocolKey)) 'idleviz:// is no longer registered'
Assert-That ($null -eq (Get-StartupCommand)) 'it does not run at startup'
Assert-That (-not (Test-Path $uninstallKey)) 'it is no longer listed as installed'

Write-Host 'The installer test passed.'
