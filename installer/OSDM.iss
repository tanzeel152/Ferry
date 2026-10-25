; OSDM installer (Inno Setup 6). Build with ..\build-installer.ps1, which publishes OSDM first.
; Installs to Program Files, adds Start menu and desktop shortcuts, an entry in Control Panel > Programs and Features
; (Settings > Apps), and optionally starts OSDM in the tray when Windows starts.

#define AppName "OSDM"
#define AppExe "OSDM.exe"
#define PublishDir AddBackslash(SourcePath) + "..\publish"
; Version comes from OSDM.exe (set in OSDM\OSDM.csproj); "0.1.0.0" -> "0.1.0".
#define FileVersion GetVersionNumbersString(PublishDir + "\" + AppExe)
#define AppVersion Copy(FileVersion, 1, RPos(".", FileVersion) - 1)

[Setup]
AppId={{C5DA9965-1D72-483D-B85F-E6008A1EDBB6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=OSDM
UninstallDisplayName={#AppName} - Open Source Download Manager
UninstallDisplayIcon={app}\{#AppExe}
VersionInfoVersion={#FileVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Program Files needs admin, like most download managers. "/CURRENTUSER" on the command line installs for this user only.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=OSDM-Setup-{#AppVersion}
SetupIconFile=..\OSDM\osdm.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; OSDM is closed by [Code] below ("OSDM.exe --quit"), so Restart Manager is not needed.
CloseApplications=no
; The startup entry and the video tools folder are per-user, on purpose.
UsedUserAreasWarning=no

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: startup; Description: "Start OSDM when Windows starts (it waits in the taskbar tray to catch downloads)"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\browser-extension\*"; DestDir: "{app}\browser-extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Open Source Download Manager"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Open Source Download Manager"; Tasks: desktopicon

[Registry]
; Same value as OSDM's "Tools > Start OSDM when Windows starts".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OSDM"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; yt-dlp and FFmpeg, downloaded by OSDM on request. Settings and the download list (%APPDATA%\OSDM) and downloaded files are kept.
Type: filesandordirs; Name: "{localappdata}\OSDM\tools"
Type: dirifempty; Name: "{localappdata}\OSDM"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StillRunning = 'OSDM is still running. Right-click its tray icon, choose Exit, then try again.';

function OSDMRunning(): Boolean;
begin
  Result := CheckForMutexes('Local\OSDM.SingleInstance');
end;

{ Asks the running OSDM to save and exit. "OSDM.exe --quit" returns once it has. }
procedure QuitOSDM(const Exe: String);
var
  ResultCode: Integer;
begin
  if OSDMRunning() and FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if OSDMRunning() then
  begin
    { Use the new OSDM.exe, so this also works when OSDM runs from somewhere else. }
    ExtractTemporaryFile('{#AppExe}');
    QuitOSDM(ExpandConstant('{tmp}\{#AppExe}'));
    if OSDMRunning() then
      Result := StillRunning;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  QuitOSDM(ExpandConstant('{app}\{#AppExe}'));
  Result := not OSDMRunning();
  if not Result then
    SuppressibleMsgBox(StillRunning, mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  { Remove the startup entry if it points to this copy (it may have been turned on from inside OSDM). }
  if (CurUninstallStep = usPostUninstall) and RegQueryStringValue(HKCU, RunKey, 'OSDM', Value) and
     (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0) then
    RegDeleteValue(HKCU, RunKey, 'OSDM');
end;
