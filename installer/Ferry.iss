; Ferry installer (Inno Setup 6). Build with ..\build-installer.ps1, which publishes Ferry first.
; Installs to Program Files, adds Start menu and desktop shortcuts, an entry in Control Panel > Programs and Features
; (Settings > Apps), and optionally starts Ferry in the tray when Windows starts.

#define AppName "Ferry"
#define AppExe "Ferry.exe"
#define AppUrl "https://github.com/tanzeel152/Ferry"
#define PublishDir AddBackslash(SourcePath) + "..\publish"
; Version comes from Ferry.exe (set in Ferry\Ferry.csproj); "0.1.0.0" -> "0.1.0".
#define FileVersion GetVersionNumbersString(PublishDir + "\" + AppExe)
#define AppVersion Copy(FileVersion, 1, RPos(".", FileVersion) - 1)

[Setup]
; OSDM, Ferry's old name, used {C5DA9965-1D72-483D-B85F-E6008A1EDBB6}. PrepareToInstall uninstalls it.
AppId={{E6E8270B-405D-486B-8C7C-73B5FCDEB5C0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Ferry contributors
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
UninstallDisplayName={#AppName} Download Manager
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
OutputBaseFilename=Ferry-Setup-{#AppVersion}
SetupIconFile=..\Ferry\ferry.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Ferry is closed by [Code] below ("Ferry.exe --quit"), so Restart Manager is not needed.
CloseApplications=no
; The startup entry and the video tools folder are per-user, on purpose.
UsedUserAreasWarning=no

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: startup; Description: "Start Ferry when Windows starts (it waits in the taskbar tray to catch downloads)"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\browser-extension\*"; DestDir: "{app}\browser-extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Download manager"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Download manager"; Tasks: desktopicon

[Registry]
; Same value as Ferry's "Tools > Start Ferry when Windows starts".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Ferry"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; yt-dlp and FFmpeg, downloaded by Ferry on request. Settings and the download list (%APPDATA%\Ferry) and downloaded files are kept.
Type: filesandordirs; Name: "{localappdata}\Ferry\tools"
Type: dirifempty; Name: "{localappdata}\Ferry"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  OsdmKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{C5DA9965-1D72-483D-B85F-E6008A1EDBB6}_is1';
  StillRunning = 'Ferry is still running. Right-click its tray icon, choose Exit, then try again.';

function FerryRunning(): Boolean;
begin
  Result := CheckForMutexes('Local\Ferry.SingleInstance');
end;

{ Asks the running Ferry to save and exit. "Ferry.exe --quit" returns once it has. }
procedure QuitFerry(const Exe: String);
var
  ResultCode: Integer;
begin
  if FerryRunning() and FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function OsdmValue(const Name: String; var Value: String): Boolean;
begin
  Result := RegQueryStringValue(HKLM, OsdmKey, Name, Value) or RegQueryStringValue(HKCU, OsdmKey, Name, Value);
end;

{ OSDM is Ferry's old name. Ferry replaces it: keep its video tools, then uninstall it quietly.
  Its settings and download list are moved by Ferry itself when it first starts (FromOsdm.cs). }
procedure RemoveOsdm();
var
  Uninstaller, Folder, OldTools, NewTools: String;
  ResultCode, Waited: Integer;
begin
  if not OsdmValue('UninstallString', Uninstaller) then Exit;
  if CheckForMutexes('Local\OSDM.SingleInstance') and OsdmValue('InstallLocation', Folder) then
    Exec(AddBackslash(Folder) + 'OSDM.exe', '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { OSDM's uninstaller deletes the video tools, so move them to Ferry's folder first. }
  OldTools := ExpandConstant('{localappdata}\OSDM\tools');
  NewTools := ExpandConstant('{localappdata}\Ferry\tools');
  if DirExists(OldTools) and not DirExists(NewTools) then
  begin
    ForceDirectories(ExtractFileDir(NewTools));
    RenameFile(OldTools, NewTools);
  end;
  Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { The uninstaller finishes in a copy of itself, so wait (up to 30 s) until its entry is gone. }
  Waited := 0;
  while OsdmValue('UninstallString', Uninstaller) and (Waited < 30000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
end;

procedure InitializeWizard();
begin
  { Keep OSDM's "Start with Windows" choice. }
  if RegValueExists(HKCU, RunKey, 'OSDM') then
    WizardSelectTasks('startup');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if FerryRunning() then
  begin
    { Use the new Ferry.exe, so this also works when Ferry runs from somewhere else. }
    ExtractTemporaryFile('{#AppExe}');
    QuitFerry(ExpandConstant('{tmp}\{#AppExe}'));
    if FerryRunning() then
    begin
      Result := StillRunning;
      Exit;
    end;
  end;
  RemoveOsdm();
end;

function InitializeUninstall(): Boolean;
begin
  QuitFerry(ExpandConstant('{app}\{#AppExe}'));
  Result := not FerryRunning();
  if not Result then
    SuppressibleMsgBox(StillRunning, mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  { Remove the startup entry if it points to this copy (it may have been turned on from inside Ferry). }
  if (CurUninstallStep = usPostUninstall) and RegQueryStringValue(HKCU, RunKey, 'Ferry', Value) and
     (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0) then
    RegDeleteValue(HKCU, RunKey, 'Ferry');
end;
