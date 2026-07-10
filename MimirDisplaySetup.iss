; Mimir Display — Inno Setup 6 installer script
;
; HOW TO BUILD LOCALLY:
;   1. Install Inno Setup 6: https://jrsoftware.org/isdl.php
;   2. Run: .\publish-single-file.ps1        (creates publish\ folder)
;   3. Open this file in Inno Setup Compiler and press Ctrl+F9
;      — or from the command line:
;        & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" MimirDisplaySetup.iss
;   Output: installer-output\MimirDisplaySetup-<version>.exe
;
; VERSION: set MyAppVersion below (or pass /DMyAppVersion=x.y.z on the CLI).
; The GitHub Actions workflow passes the tag version automatically.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName      "Mimir Display"
#define MyAppPublisher "Mimir"
#define MyAppURL       "https://github.com/ryanmimircloud/mimir-display-win"
#define MyAppExeName   "MimirDisplay.exe"
#define SourcePath     "publish"

[Setup]
; AppId uniquely identifies this application — do NOT change after first release.
AppId={{BCD9AA7D-D6B9-4CDE-8BF3-4F42CA6D42E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Install to Program Files without requiring elevation (per-user or per-machine)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output
OutputDir=installer-output
OutputBaseFilename=MimirDisplaySetup-{#MyAppVersion}
SetupIconFile=MimirDisplay\Resources\mimir.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; UI
WizardStyle=modern
DisableWelcomePage=no
LicenseFile=

; Min Windows version: Windows 10 (10.0)
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "{cm:CreateDesktopIcon}";         GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry";  Description: "Start Mimir Display when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; ── Main executable (single-file, self-contained — no separate DLLs needed) ──
Source: "{#SourcePath}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; ── Default configuration (appsettings.json) ──
; Always install the base settings; preserve any user edits on upgrade.
Source: "{#SourcePath}\appsettings.json"; DestDir: "{app}"; \
  Flags: ignoreversion onlyifdoesntexist

; ── Environment template (.env.example → .env if not already present) ──
; Ships the example file; the Code section copies it to .env on first install.
Source: "MimirDisplay\.env.example"; DestDir: "{app}"; \
  DestName: ".env.example"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";                        Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Run-at-startup via HKCU (no elevation required)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Offer to launch after install
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove log/cache/state directories written by the app
Type: filesandordirs; Name: "{localappdata}\MimirDisplay"

[Code]
// On first install, copy .env.example → .env so the app has a writable config.
procedure CurStepChanged(CurStep: TSetupStep);
var
  EnvExample, EnvDest: string;
begin
  if CurStep = ssPostInstall then
  begin
    EnvExample := ExpandConstant('{app}\.env.example');
    EnvDest    := ExpandConstant('{app}\.env');
    if FileExists(EnvExample) and not FileExists(EnvDest) then
      FileCopy(EnvExample, EnvDest, False);
  end;
end;
