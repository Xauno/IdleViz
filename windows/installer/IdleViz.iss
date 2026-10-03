; Inno Setup script for the IdleViz setup file. Built by ..\build-installer.ps1, which passes
; AppVersion and PublishDir. Per-user install: no admin prompt, files under %LOCALAPPDATA%\Programs.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
; Identifies the app to Windows across versions. Never change it, or updates install side by side.
AppId={{6F0B2C1E-7C0B-4E0B-9B43-1D2A6B7C9E55}
AppName=IdleViz
AppVersion={#AppVersion}
AppPublisher=Xauno
DefaultDirName={localappdata}\Programs\IdleViz
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=IdleViz-Setup
SetupIconFile=..\IdleViz.App\Assets\IdleViz.ico
UninstallDisplayIcon={app}\IdleViz.exe
UninstallDisplayName=IdleViz
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The app has no window for Windows to ask to close, so the [Code] section stops it instead.
CloseApplications=no

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; Files a newer version no longer ships would otherwise stay behind.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{userprograms}\IdleViz"; Filename: "{app}\IdleViz.exe"

[Registry]
; The idleviz:// protocol, for this user only, so `start idleviz://open` reaches the app.
Root: HKCU; Subkey: "Software\Classes\idleviz"; ValueType: string; ValueData: "URL:IdleViz"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\idleviz"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\idleviz\shell\open\command"; ValueType: string; ValueData: """{app}\IdleViz.exe"" ""%1"""
; "Run at startup" is written by the app itself. Setup only takes it away again on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "IdleViz"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\IdleViz.exe"; Description: "Start IdleViz"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM IdleViz.exe"; Flags: runhidden; RunOnceId: "StopIdleViz"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // A running copy holds its files open. Stop it; a failure just means it wasn't running.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM IdleViz.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
