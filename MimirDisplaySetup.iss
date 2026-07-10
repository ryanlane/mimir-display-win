; Mimir Display Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isdl.php
; 
; INSTRUCTIONS:
; 1. Install Inno Setup
; 2. Run .\publish-single-file.ps1 to create the single-file EXE
; 3. Replace {YOUR-GUID-HERE} below with a new GUID
; 4. Open this file in Inno Setup Compiler
; 5. Click Build → Compile to create setup.exe

#define MyAppName "Mimir Display"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Mimir"
#define MyAppURL "https://github.com/yourusername/mimir-display-win"
#define MyAppExeName "MimirDisplay.exe"

; Single-file publish output folder
#define SourcePath "publish"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; Generate a new GUID here: https://www.guidgenerator.com/
AppId={{YOUR-GUID-HERE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Uncomment the following line to run in non-administrative install mode
; (install for current user only.)
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=MimirDisplaySetup
SetupIconFile=MimirDisplay\Resources\mimir.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Single-file executable (contains all DLLs and dependencies)
Source: "{#SourcePath}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Configuration files
Source: "{#SourcePath}\.env"; DestDir: "{app}"; Flags: ignoreversion confirmoverwrite
Source: "{#SourcePath}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion; Check: FileExists(ExpandConstant('{#SourcePath}\appsettings.json'))

; NOTE: When using PublishSingleFile=true, you only need the .exe and config files.
; All DLLs are bundled inside MimirDisplay.exe

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MimirDisplay"

[Code]
function FileExists(const FileName: string): Boolean;
begin
  Result := FileOrDirExists(FileName);
end;
