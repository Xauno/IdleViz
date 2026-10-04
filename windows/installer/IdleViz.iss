; Inno Setup script for the IdleViz setup file. Built by ..\build-installer.ps1, which passes
; AppVersion and PublishDir. Per-user install: no admin prompt, files under %LOCALAPPDATA%\Programs
; unless the user picks another folder. The wizard shows a welcome page, the licence, the folder
; (first install only) and the options below. test-installer.ps1 checks what it leaves behind.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
; Identifies the app to Windows across versions. Never change it, or updates install side by side.
#define AppGuid "6F0B2C1E-7C0B-4E0B-9B43-1D2A6B7C9E55"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{{#AppGuid}}
AppName=IdleViz
AppVersion={#AppVersion}
AppPublisher=Xauno
DefaultDirName={localappdata}\Programs\IdleViz
PrivilegesRequired=lowest
DisableWelcomePage=no
LicenseFile=..\..\LICENSE
; An update goes into the folder of the installed copy without asking, so there is never a second copy.
DisableDirPage=auto
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

[Messages]
WelcomeLabel2=This will install [name/ver] for your Windows account. No administrator rights are needed.%n%nIdleViz shows a full-screen music visualizer that moves to Spotify's sound, with the current track on top. It opens when the PC has been idle for a while or when you press a hotkey, and closes on any input.%n%nIf IdleViz is running, Setup closes it first.

[Tasks]
Name: "startmenu"; Description: "Start menu entry"
Name: "desktopicon"; Description: "Desktop shortcut"; Flags: unchecked
; On an update the [Code] section sets this one from the registry, since the app's own switch may have changed it.
Name: "runatstartup"; Description: "Run at startup"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; Files a newer version no longer ships would otherwise stay behind. Only in a folder that holds
; IdleViz already: the user may have picked a folder with other things in it.
Type: filesandordirs; Name: "{app}\*"; Check: FolderHoldsIdleViz
; A shortcut from an earlier install whose box is no longer ticked.
Type: files; Name: "{userprograms}\IdleViz.lnk"; Tasks: not startmenu
Type: files; Name: "{userdesktop}\IdleViz.lnk"; Tasks: not desktopicon

[UninstallDelete]
; Setup only removes folders it made itself. After an update the folder was already there (and the
; record of the first install was cleared with the old files), so it would stay behind, empty.
Type: dirifempty; Name: "{app}"

[Icons]
Name: "{userprograms}\IdleViz"; Filename: "{app}\IdleViz.exe"; Tasks: startmenu
Name: "{userdesktop}\IdleViz"; Filename: "{app}\IdleViz.exe"; Tasks: desktopicon

[Registry]
; The idleviz:// protocol, for this user only, so `start idleviz://open` reaches the app.
Root: HKCU; Subkey: "Software\Classes\idleviz"; ValueType: string; ValueData: "URL:IdleViz"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\idleviz"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\idleviz\shell\open\command"; ValueType: string; ValueData: """{app}\IdleViz.exe"" ""%1"""
; "Run at startup": the same value the switch in the app's settings writes (StartupEntry.Command).
; Unticked, the [Code] section removes it. The second line takes it away on uninstall either way.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "IdleViz"; ValueData: """{app}\IdleViz.exe"""; Tasks: runatstartup
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "IdleViz"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\IdleViz.exe"; Description: "Start IdleViz"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM IdleViz.exe"; Flags: runhidden; RunOnceId: "StopIdleViz"

[Code]
var
  StartupTaskSet: Boolean;

function FolderHoldsIdleViz: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\IdleViz.exe'));
end;

function IsUpdate: Boolean;
begin
  Result := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppGuid}' + '}_is1');
end;

// Whether the Run key starts the copy in the chosen folder. An entry for another copy, such as a
// Debug build, is not this one's.
function RunsAtStartup: Boolean;
var
  Command: String;
begin
  Result := RegQueryStringValue(HKCU, '{#RunKey}', 'IdleViz', Command)
    and (CompareText(Trim(Command), '"' + AddBackslash(WizardDirValue) + 'IdleViz.exe"') = 0);
end;

function TasksGivenOnCommandLine: Boolean;
var
  Tail: String;
begin
  Tail := Uppercase(GetCmdTail);
  Result := (Pos('/TASKS=', Tail) > 0) or (Pos('/MERGETASKS=', Tail) > 0);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // The first time the options show on an update, "Run at startup" is what the app's switch last
  // left, not what was ticked at the last install. A silent install passes through here too.
  if (CurPageID = wpSelectTasks) and not StartupTaskSet then
  begin
    StartupTaskSet := True;
    if IsUpdate and not TasksGivenOnCommandLine then
    begin
      if RunsAtStartup then
        WizardSelectTasks('runatstartup')
      else
        WizardSelectTasks('!runatstartup');
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('runatstartup') and RunsAtStartup then
    RegDeleteValue(HKCU, '{#RunKey}', 'IdleViz');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // A running copy holds its files open. Stop it; a failure just means it wasn't running.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM IdleViz.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
