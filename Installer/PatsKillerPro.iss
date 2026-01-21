; PatsKiller Pro Installer Script
; Inno Setup 6.x

#define MyAppName "PatsKiller Pro"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "PatsKiller"
#define MyAppURL "https://patskiller.com"
#define MyAppExeName "PatsKillerPro.exe"

[Setup]
AppId={{A7B8C9D0-1234-5678-ABCD-EF0123456789}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/support
AppUpdatesURL={#MyAppURL}/downloads
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=license.txt
OutputDir=Output
OutputBaseFilename=PatsKillerPro_Setup_v{#MyAppVersion}
SetupIconFile=..\PatsKillerPro\Resources\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Ford & Lincoln PATS Key Programming Solution
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Main application files - adjust source path as needed
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Icon files
Source: "..\PatsKillerPro\Resources\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatsKillerPro\Resources\logo.png"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DotNetPage: TOutputMsgMemoWizardPage;
  NeedsDotNet: Boolean;

function IsDotNet8Installed(): Boolean;
var
  ResultCode: Integer;
begin
  // Check if .NET 8 runtime is installed
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if Result then
  begin
    // Further check for .NET 8 specifically would require parsing output
    // For simplicity, we assume if dotnet works, we're good (self-contained app)
    Result := True;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  NeedsDotNet := False;
  
  // Check for .NET 8
  if not IsDotNet8Installed() then
  begin
    NeedsDotNet := True;
    // We'll show a message but continue since app is self-contained
  end;
end;

procedure InitializeWizard();
begin
  if NeedsDotNet then
  begin
    DotNetPage := CreateOutputMsgMemoPage(wpInfoBefore,
      '.NET Runtime Information',
      'The application includes all necessary runtime components.',
      'Note: This application is self-contained and includes the .NET 8 runtime.' + #13#10 +
      'No additional downloads are required.' + #13#10 + #13#10 +
      'If you experience any issues, you can download the .NET 8 runtime from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet/8.0',
      '');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create app data folder for logs
    ForceDirectories(ExpandConstant('{userappdata}\PatsKiller Pro\Logs'));
  end;
end;
