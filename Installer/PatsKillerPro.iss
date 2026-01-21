; PatsKiller Pro Installer Script
; Inno Setup 6.x
; Website: https://patskiller.com

#define MyAppName "PatsKiller Pro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PatsKiller"
#define MyAppURL "https://patskiller.com"
#define MyAppExeName "PatsKillerPro.exe"
#define MyAppSupportEmail "support@patskiller.com"

[Setup]
; Application Info
AppId={{B3C4D5E6-F7A8-9B0C-1D2E-3F4A5B6C7D8E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/support
AppUpdatesURL={#MyAppURL}/downloads
AppContact={#MyAppSupportEmail}

; Installation Directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output Settings
OutputDir=Output
OutputBaseFilename=PatsKillerPro_Setup_v{#MyAppVersion}
SetupIconFile=..\PatsKillerPro\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Visual Settings
WizardStyle=modern
WizardResizable=no

; Requirements
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; Digital Signature (uncomment when you have a certificate)
; SignTool=signtool
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
; Main Application
Source: "..\PatsKillerPro\bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatsKillerPro\bin\Release\net8.0-windows\win-x64\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PatsKillerPro\bin\Release\net8.0-windows\win-x64\publish\*.json"; DestDir: "{app}"; Flags: ignoreversion

; Resources
Source: "..\PatsKillerPro\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation
Source: "..\..\README.md"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion
Source: "..\..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up application data on uninstall (optional - ask user)
Type: filesandordirs; Name: "{userappdata}\PatsKiller Pro"

[Registry]
; Application registration
Root: HKLM; Subkey: "SOFTWARE\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Code]
// Check for .NET 8 Runtime
function IsDotNet8Installed: Boolean;
var
  ResultCode: Integer;
begin
  // Check if dotnet command succeeds with --list-runtimes
  Result := Exec('cmd.exe', '/c dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 8"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  
  // Check for .NET 8 Runtime
  if not IsDotNet8Installed then
  begin
    if MsgBox('PatsKiller Pro requires .NET 8 Desktop Runtime.' + #13#10 + #13#10 +
              'Would you like to download it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;

// Custom welcome page text
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := 
    'This will install PatsKiller Pro v{#MyAppVersion} on your computer.' + #13#10 + #13#10 +
    'PatsKiller Pro is a professional Ford & Lincoln PATS key programming solution.' + #13#10 + #13#10 +
    'Requirements:' + #13#10 +
    '• Windows 10/11 (64-bit)' + #13#10 +
    '• .NET 8 Desktop Runtime' + #13#10 +
    '• J2534 v2 compatible device' + #13#10 + #13#10 +
    'Website: {#MyAppURL}' + #13#10 +
    'Support: {#MyAppSupportEmail}';
end;

// Show finished page with website link
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption := 
      'Setup has finished installing PatsKiller Pro on your computer.' + #13#10 + #13#10 +
      'Visit patskiller.com/calculator to convert outcodes to incodes.' + #13#10 + #13#10 +
      'For support, contact: {#MyAppSupportEmail}';
  end;
end;
