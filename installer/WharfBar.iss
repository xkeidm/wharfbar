; WharfBar installer — Inno Setup 6 script.
;
; Do not compile this by hand for a release: installer/build-release.ps1 passes the defines
; below and the release workflow (.github/workflows/release.yml) calls that script.
;
;   ISCC.exe WharfBar.iss /DSourceDir=<published payload> /DOutputDir=<out dir>
;            /DAppVersion=<semver, e.g. 1.0.0-beta.2> /DNumericVersion=<x.y.z.0>
;            /DOutputBaseName=<file stem>
;
; Design notes:
;  - Per-user install (PrivilegesRequired=lowest): no UAC, no admin, and upgrades/uninstall do
;    not need elevation. The app only touches per-user shell state (AppBar, Startup shortcut,
;    %AppData%\WharfBar), so a machine-wide install buys nothing.
;  - The running app is closed with `WharfBar.exe --quit`, never force-killed: a hard kill skips
;    the AppBar ABM_REMOVE and leaves the reserved top-bar strip on screen until Explorer
;    restarts (docs/04-gotchas.md). Setup therefore refuses to continue while an instance that
;    cannot be asked to quit is still running.
;  - The app owns autostart itself (Settings -> General -> Start with Windows keeps a Startup
;    shortcut); the installer does not create one, and uninstall removes the one the app made.

#define AppName "WharfBar"
#define AppPublisher "WharfBar contributors"
#define AppUrl "https://github.com/xkeidm/wharfbar"
#define AppReleasesUrl "https://github.com/xkeidm/wharfbar/releases"

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0-beta.3"
#endif

#ifndef NumericVersion
  #define NumericVersion "1.0.0.0"
#endif

#ifndef OutputBaseName
  #define OutputBaseName "WharfBar-Setup-" + AppVersion + "-x64"
#endif

[Setup]
; Stable across releases: Inno uses it to recognise an in-place upgrade instead of a second app.
AppId={{3F1C0E7A-9B24-4B7E-8F3D-5C1A2D4E6F80}
AppName={#AppName}
; Numeric on purpose (VersionInfoVersion / Add-Remove-Programs version); the SemVer string is the
; display name below.
AppVersion={#NumericVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppReleasesUrl}
AppUpdatesURL={#AppReleasesUrl}
DefaultDirName={localappdata}\Programs\WharfBar
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile=..\src\WharfBar\Assets\WharfBar.ico
UninstallDisplayIcon={app}\WharfBar.exe
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 (19041) is the app's SupportedOSPlatformVersion.
MinVersion=10.0.19041
PrivilegesRequired=lowest
; Closed by [Code] below with --quit, never by Restart Manager: a forced close leaves the AppBar
; strip reserved. Also means Setup never restarts the app after an upgrade.
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=MIT License

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Payload: the self-contained single-file WharfBar.exe plus Wallpapers\ (bundled gallery) and
; LICENSE. Everything is per-user, so no shared DLLs and no registration.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\WharfBar.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\WharfBar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WharfBar.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Runs before the files are removed: the app restores the taskbar and removes its AppBars, so the
; uninstaller can delete the binary without scheduling a delete-on-reboot.
Filename: "{app}\WharfBar.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitWharfBar"

[UninstallDelete]
; The app creates this Startup shortcut itself (Settings -> General -> Start with Windows).
Type: files; Name: "{userstartup}\WharfBar.lnk"

[Code]
const
  { Same object SingleInstanceGuard creates; CheckForMutexes can see the Local\ prefix. }
  WharfBarMutex = 'Local\WharfBar.SingleInstance.{0F4C6A1E-9C4B-4C39-9E1B-6B4E2E1D7A31}';
  { Bounded wait after the quit request: the app restores the taskbar and removes its AppBars
    before it releases this mutex (App.OnExit releases it last). }
  QuitPollAttempts = 60;
  QuitPollIntervalMs = 250;

function WharfBarStillRunning(): Boolean;
begin
  Result := CheckForMutexes(WharfBarMutex);
end;

function StopWharfBar(): String;
var
  AppExe: String;
  ResultCode: Integer;
  Attempt: Integer;
begin
  Result := '';

  { Nothing to stop: the normal first-install / not-running path. }
  if not WharfBarStillRunning() then
    Exit;

  AppExe := ExpandConstant('{app}\WharfBar.exe');
  if FileExists(AppExe) then
  begin
    { Graceful request. The exit code alone is not proof: a pre---quit build starts a doomed
      second instance, fails its single-instance guard and exits 0 while the real instance keeps
      running. So always confirm the single-instance mutex is gone before touching any file. }
    if not Exec(AppExe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := 'WharfBar could not be closed automatically.' + #13#10 + #13#10 +
                'Quit it from the WharfBar tray icon (Quit WharfBar), then run Setup again.';
      Exit;
    end;
  end;

  for Attempt := 1 to QuitPollAttempts do
  begin
    if not WharfBarStillRunning() then
      Exit;
    Sleep(QuitPollIntervalMs);
  end;

  Result := 'WharfBar is still running and did not quit.' + #13#10 + #13#10 +
            'Quit it from the WharfBar tray icon (Quit WharfBar), then run Setup again.' + #13#10 +
            'Setup never force-closes WharfBar: a forced close leaves the reserved space ' +
            'along the top of the screen in place until Explorer restarts.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := StopWharfBar();
end;
