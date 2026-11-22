; DiskChecker Windows installer; consumes dotnet publish --runtime win-x64 output.
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "DiskChecker"
#define MyAppPublisher "Afrowave Community"
#define MyAppURL "https://github.com/afrowaveltd/DiskChecker"
#define MyAppExeName "DiskChecker.UI.Avalonia.exe"

[Setup]
AppId={{A7B8C9D0-E1F2-3456-7890-ABCDEF123456}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
PrivilegesRequired=admin
OutputDir=..\packages
OutputBaseFilename=DiskChecker-{#MyAppVersion}-win-x64-Setup
SetupIconFile=..\DiskChecker.UI.Avalonia\Assets\icon.ico
Compression=lzma2
SolidCompression=yes
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained build: no system .NET runtime required.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Intentionally do not launch automatically at installer completion.
; Installation can run under another administrator's credentials. Running the
; app then would create configuration/PDFs in that administrator's profile.
; The packaged Windows executable carries requireAdministrator in app.manifest.

[Code]
// Smartmontools is a mandatory diagnostic dependency. Do not replace an
// existing working installation or download unchecked arbitrary executables.
// Windows Package Manager validates the official winget manifest hashes.
function SmartctlWorksAt(const FileName: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := False;
  if not FileExists(FileName) then
    Exit;
  Result := Exec(FileName, '--version', '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function SmartctlAvailable: Boolean;
var
  ExitCode: Integer;
begin
  Result := SmartctlWorksAt(ExpandConstant('{pf64}\smartmontools\bin\smartctl.exe')) or
    SmartctlWorksAt(ExpandConstant('{pf32}\smartmontools\bin\smartctl.exe'));
  if Result then
    Exit;
  // Also respect a valid pre-existing custom smartctl installation in PATH.
  Result := Exec(ExpandConstant('{cmd}'),
    '/D /C "smartctl --version >NUL 2>&1"', '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Ran: Boolean;
begin
  Result := '';
  if SmartctlAvailable then
  begin
    Log('DiskChecker: existing smartctl verified; skipping installation.');
    Exit;
  end;

  Log('DiskChecker: smartctl absent; installing smartmontools with WinGet.');
  // WinGet uses the published manifest and its installer SHA256 validation.
  // Do not bypass hash checks or change the user's configured repositories.
  Ran := Exec(ExpandConstant('{cmd}'),
    '/D /C winget install --exact --id smartmontools.smartmontools ' +
    '--source winget --scope machine --silent ' +
    '--accept-package-agreements --accept-source-agreements',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  if not Ran or (ExitCode <> 0) then
  begin
    Log('DiskChecker: smartmontools installation failed or WinGet unavailable.');
    Result := 'DiskChecker requires smartmontools (smartctl). ' +
      'Automatic installation failed or Windows Package Manager is unavailable. ' +
      'Install smartmontools.smartmontools using WinGet or the official ' +
      'smartmontools installer, then start DiskChecker setup again.';
    Exit;
  end;

  if not SmartctlAvailable then
  begin
    Log('DiskChecker: WinGet finished, but smartctl could not be verified.');
    Result := 'smartmontools installation did not provide a working smartctl.exe. ' +
      'Please verify the installed tool and retry DiskChecker setup.';
    Exit;
  end;

  Log('DiskChecker: smartmontools installation and smartctl verified.');
end;
