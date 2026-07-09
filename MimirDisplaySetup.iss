; Mimir Display Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isdl.php
; 
; INSTRUCTIONS:
; 1. Install Inno Setup
; 2. Update the paths below to match your build output
; 3. Open this file in Inno Setup Compiler
; 4. Click Build → Compile to create setup.exe

#define MyAppName "Mimir Display"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Mimir"
#define MyAppURL "https://github.com/yourusername/mimir-display-win"
#define MyAppExeName "MimirDisplay.exe"

; Update this path to your actual publish output folder
#define SourcePath "MimirDisplay\bin\Release\net8.0-windows\win-x64\publish"

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
; Main executable
Source: "{#SourcePath}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Configuration files
Source: "{#SourcePath}\.env"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion; Check: FileExists(ExpandConstant('{#SourcePath}\appsettings.json'))

; Any other files in the publish folder (if not using single-file publish)
; Uncomment if you're not using PublishSingleFile:
; Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
