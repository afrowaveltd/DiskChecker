; DiskChecker Inno Setup Script
; Creates Windows installer for DiskChecker

#define MyAppName "DiskChecker"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DiskChecker"
#define MyAppURL "https://github.com/diskchecker/diskchecker"
#define MyAppExeName "DiskChecker.UI.Avalonia.exe"

[Setup]
; Basic info
AppId={{A7B8C9D0-E1F2-3456-7890-ABCDEF123456}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation settings
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE.txt
PrivilegesRequired=admin
OutputDir=..\installer
OutputBaseFilename=DiskChecker-{#MyAppVersion}-Setup
SetupIconFile=DiskChecker.UI.Avalonia\Assets\avalonia-logo.ico
Compression=lzma
SolidCompression=yes

; Windows version requirements
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main Avalonia application
Source: "publish\avalonia-win\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Check for .NET runtime
function IsDotNetInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  
  // Check for admin rights
  if not IsAdmin() then
  begin
    MsgBox('DiskChecker vyžaduje administrátorská práva pro přístup k SMART datům disků.' + #13#10 + 
           'Prosím spusťte instalátor jako správce.', mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Copy database file to user's AppData
    CreateDir(ExpandConstant('{userappdata}\DiskChecker'));
    if FileExists(ExpandConstant('{app}\DiskChecker.db')) then
    begin
      if not FileExists(ExpandConstant('{userappdata}\DiskChecker\DiskChecker.db')) then
      begin
        FileCopy(ExpandConstant('{app}\DiskChecker.db'), 
                 ExpandConstant('{userappdata}\DiskChecker\DiskChecker.db'), False);
      end;
    end;
  end;
end;
