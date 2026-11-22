; DiskChecker Windows installer; consumes dotnet publish --runtime win-x64 output.
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "DiskChecker"
#define MyAppPublisher "Afrowave Community"
#define MyAppURL "https://github.com/afrowaveltd/DiskChecker"
#define MyAppExeName "DiskChecker.UI.Avalonia.exe"
#define SmartVersion "8.0-623-gd788ad8c38ee"
#define SmartPackage "smartmontools-win32-setup-8.0-623-gd788ad8c38ee.exe"

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

[CustomMessages]
english.SmartPageTitle=smartmontools - required disk diagnostics
english.SmartPageSubtitle=Review the installed smartctl version and choose an upgrade if available.
english.SmartRequired=Install smartmontools (required):
english.SmartAbsent=No working smartctl.exe was found. The included official prerelease will be installed automatically. An internet connection is not required.
english.SmartSame=Installed version is current:
english.SmartSameNote=The identical bundled version is installed and working. No changes will be made.
english.SmartNewer=Newer smartmontools installed - keep existing version
english.SmartNewerNote=The installed version is newer than the bundled test build. DiskChecker will keep it.
english.SmartOlder=Upgrade smartmontools to
english.SmartUnknown=Replace existing version with
english.SmartOptionalNote=An existing working smartctl was detected. You can decline the update; the installer will preserve it.
english.SmartDetected=Detected executable:
english.SmartVersion=Installed version:
czech.SmartPageTitle=smartmontools - nutna diagnosticka soucast
czech.SmartPageSubtitle=Kontrola nainstalovane verze smartctl a pripadna aktualizace.
czech.SmartRequired=Instalovat smartmontools (povinne):
czech.SmartAbsent=Nebyl nalezen funkcni smartctl.exe. Oficialni testovaci verze se nainstaluje automaticky z balicku. Internet neni potreba.
czech.SmartSame=Aktualni verze je jiz nainstalovana:
czech.SmartSameNote=Shodna verze smartmontools funguje. Nebude zmenena.
czech.SmartNewer=Novější smartmontools je uz nainstalovan - ponechat
czech.SmartNewerNote=Nainstalovana verze je novejsi nez prilozena testovaci verze. Neprovedeme downgrade.
czech.SmartOlder=Aktualizovat smartmontools na
czech.SmartUnknown=Nahradit stavajici verzi za
czech.SmartOptionalNote=Byla nalezena funkcni instalace. Aktualizaci muzete odmitnout a zachovat stavajici verzi.
czech.SmartDetected=Nalezeny soubor:
czech.SmartVersion=Zjistena verze:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained build: no system .NET runtime required.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Official upstream installer is embedded offline and only extracted when needed.
; Its SHA-256 is checked at package-build time; no network is needed on client PCs.
Source: "vendor\{#SmartPackage}"; Flags: dontcopy
; GNU GPL v2 license for the redistributed, unmodified upstream smartmontools installer.
Source: "vendor\smartmontools-COPYING.txt"; DestDir: "{app}\licenses\smartmontools"; DestName: "COPYING.txt"; Flags: ignoreversion
Source: "vendor\smartmontools-{#SmartVersion}.tar.gz"; DestDir: "{app}\licenses\smartmontools"; DestName: "source.tar.gz"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Intentionally do not launch automatically at installer completion.
; Installation can run under another administrator's credentials. Running the
; app then would create configuration/PDFs in that administrator's profile.
; The packaged Windows executable carries requireAdministrator in app.manifest.

[Code]
const
  SmartUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\smartmontools';
  SmartTargetMajor = 8;
  SmartTargetMinor = 0;
  SmartTargetBuild = 623;

var
  SmartOptionsPage: TWizardPage;
  SmartInstallBox: TNewCheckBox;
  SmartExplanation: TNewStaticText;
  SmartCurrentPath: String;
  SmartCurrentVersion: String;
  SmartFound: Boolean;

// Never determine availability from registry entries alone. A valid,
// executable smartctl is required; trust only machine-wide locations because
// setup may be running under another administrator's UAC account.
function SmartctlWorksAt(const FileName: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := False;
  if not FileExists(FileName) then Exit;
  Result := Exec(FileName, '--version', '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function FindWorkingSmartctl: String;
var
  InstallDir, Candidate: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, SmartUninstallKey, 'InstallLocation', InstallDir) and
     (InstallDir <> '') then
  begin
    Candidate := AddBackslash(InstallDir) + 'bin\smartctl.exe';
    if SmartctlWorksAt(Candidate) then
    begin
      Result := Candidate;
      Exit;
    end;
  end;
  Candidate := ExpandConstant('{pf64}\smartmontools\bin\smartctl.exe');
  if SmartctlWorksAt(Candidate) then
  begin
    Result := Candidate;
    Exit;
  end;
  Candidate := ExpandConstant('{pf32}\smartmontools\bin\smartctl.exe');
  if SmartctlWorksAt(Candidate) then
  begin
    Result := Candidate;
    Exit;
  end;
  // Do not execute a random smartctl.exe found on user-controlled PATH
  // with elevated installer privileges. Only the HKLM installation path
  // or protected Program Files locations are trusted.
  Result := '';
end;

// Gets the CLI-reported base version when a portable installation has no
// registered DisplayVersion. The complete prerelease identity is provided
// by the upstream NSIS registry key when installed conventionally.
function SmartctlCliVersion(const Exe: String): String;
var
  TempFile, CmdArgs, Output: String;
  OutputAnsi: AnsiString;
  Code, P: Integer;
begin
  Result := '';
  TempFile := ExpandConstant('{tmp}\diskchecker-smartctl-version.txt');
  CmdArgs := '/D /S /C "' + AddQuotes(Exe) +
    ' --version > ' + AddQuotes(TempFile) + ' 2>&1"';
  if Exec(ExpandConstant('{cmd}'), CmdArgs, '', SW_HIDE,
    ewWaitUntilTerminated, Code) and (Code = 0) and
    LoadStringFromFile(TempFile, OutputAnsi) then
  begin
    Output := String(OutputAnsi);
    P := Pos('smartctl ', Output);
    if P > 0 then
    begin
      Output := Copy(Output, P + 9, 80);
      P := Pos(' ', Output);
      if P > 0 then Output := Copy(Output, 1, P - 1);
      Result := Trim(Output);
    end;
  end;
  if FileExists(TempFile) then DeleteFile(TempFile);
end;

function NextNumber(const S: String; var AtPos: Integer): Integer;
var
  StartAt: Integer;
begin
  StartAt := AtPos;
  while (AtPos <= Length(S)) and (S[AtPos] >= '0') and
    (S[AtPos] <= '9') do Inc(AtPos);
  Result := -1;
  if AtPos > StartAt then
    Result := StrToIntDef(Copy(S, StartAt, AtPos - StartAt), -1);
end;

// Comparison: -2 unknown/different same-build snapshot, -1 older,
// 0 exact bundled version, 1 newer. Do not downgrade a stable 8.0
// or a future 8.0 development build to the pinned 8.0 snapshot.
function CompareSmartVersion(const Version: String): Integer;
var
  I, Major, Minor, Build: Integer;
begin
  Result := -2;
  if CompareText(Trim(Version), '{#SmartVersion}') = 0 then
  begin
    Result := 0;
    Exit;
  end;
  I := 1;
  Major := NextNumber(Version, I);
  if (Major < 0) or (I > Length(Version)) or
    (Version[I] <> '.') then Exit;
  Inc(I);
  Minor := NextNumber(Version, I);
  if Minor < 0 then Exit;

  if (Major < SmartTargetMajor) or
    ((Major = SmartTargetMajor) and (Minor < SmartTargetMinor)) then
  begin
    Result := -1;
    Exit;
  end;
  if (Major > SmartTargetMajor) or
    ((Major = SmartTargetMajor) and (Minor > SmartTargetMinor)) then
  begin
    Result := 1;
    Exit;
  end;

  // Bare '8.0' is ambiguous (portable nightly or final release), so
  // never label it exact or older without a build identity.
  if (I > Length(Version)) or (Version[I] <> '-') then Exit;
  Inc(I);
  Build := NextNumber(Version, I);
  if Build < 0 then Exit;
  if Build < SmartTargetBuild then Result := -1
  else if Build > SmartTargetBuild then Result := 1
  else Result := -2; // same counter but another commit: not the same build.
end;

procedure RefreshSmartStatus;
var
  InstallDir, RegisteredPath, RegisteredVersion: String;
begin
  SmartCurrentPath := FindWorkingSmartctl();
  SmartFound := SmartCurrentPath <> '';
  SmartCurrentVersion := '';
  if not SmartFound then Exit;

  // Trust a registry version only when its installed binary actually works.
  if RegQueryStringValue(HKLM, SmartUninstallKey, 'InstallLocation', InstallDir) and
    (InstallDir <> '') then
  begin
    RegisteredPath := AddBackslash(InstallDir) + 'bin\smartctl.exe';
    if CompareText(RegisteredPath, SmartCurrentPath) = 0 then
    begin
      if RegQueryStringValue(HKLM, SmartUninstallKey, 'DisplayVersion',
          RegisteredVersion) then
        SmartCurrentVersion := Trim(RegisteredVersion);
    end;
  end;

  if SmartCurrentVersion = '' then
    SmartCurrentVersion := SmartctlCliVersion(SmartCurrentPath);
  Log('smartmontools: path=' + SmartCurrentPath +
    '; version=' + SmartCurrentVersion);
end;

procedure InitializeWizard;
var
  Comparison: Integer;
begin
  RefreshSmartStatus();
  SmartOptionsPage := CreateCustomPage(wpSelectTasks,
    ExpandConstant('{cm:SmartPageTitle}'),
    ExpandConstant('{cm:SmartPageSubtitle}'));
  SmartInstallBox := TNewCheckBox.Create(WizardForm);
  SmartInstallBox.Parent := SmartOptionsPage.Surface;
  SmartInstallBox.Left := ScaleX(0);
  SmartInstallBox.Top := ScaleY(12);
  SmartInstallBox.Width := SmartOptionsPage.SurfaceWidth;
  SmartInstallBox.Height := ScaleY(30);

  SmartExplanation := TNewStaticText.Create(WizardForm);
  SmartExplanation.Parent := SmartOptionsPage.Surface;
  SmartExplanation.Left := ScaleX(0);
  SmartExplanation.Top := ScaleY(54);
  SmartExplanation.Width := SmartOptionsPage.SurfaceWidth;
  SmartExplanation.Height := ScaleY(125);
  SmartExplanation.AutoSize := False;
  SmartExplanation.WordWrap := True;

  if not SmartFound then
  begin
    Log('smartmontools: state=missing, dependency installation mandatory');
    SmartInstallBox.Caption := ExpandConstant('{cm:SmartRequired}') + ' {#SmartVersion}';
    SmartInstallBox.Checked := True;
    SmartInstallBox.Enabled := False;
    SmartExplanation.Caption :=
      ExpandConstant('{cm:SmartAbsent}');
    Exit;
  end;

  Comparison := CompareSmartVersion(SmartCurrentVersion);
  if Comparison = 0 then
  begin
    Log('smartmontools: state=exact, checkbox disabled');
    SmartInstallBox.Caption := ExpandConstant('{cm:SmartSame}') + ' {#SmartVersion}';
    SmartInstallBox.Checked := False;
    SmartInstallBox.Enabled := False;
    SmartExplanation.Caption :=
      ExpandConstant('{cm:SmartSameNote}');
  end
  else if Comparison = 1 then
  begin
    Log('smartmontools: state=newer, checkbox disabled');
    SmartInstallBox.Caption := ExpandConstant('{cm:SmartNewer}');
    SmartInstallBox.Checked := False;
    SmartInstallBox.Enabled := False;
    SmartExplanation.Caption :=
      ExpandConstant('{cm:SmartNewerNote}') + ' ' + SmartCurrentVersion;
  end
  else
  begin
    if Comparison = -1 then
      Log('smartmontools: state=older, optional upgrade preselected')
    else
      Log('smartmontools: state=unknown, replacement disabled by default');
    SmartInstallBox.Checked := (Comparison = -1);
    SmartInstallBox.Enabled := True;
    if Comparison = -1 then
      SmartInstallBox.Caption := ExpandConstant('{cm:SmartOlder}') + ' {#SmartVersion}'
    else
      SmartInstallBox.Caption :=
        ExpandConstant('{cm:SmartUnknown}') + ' {#SmartVersion}';
    SmartExplanation.Caption :=
      ExpandConstant('{cm:SmartDetected}') + ' ' + SmartCurrentPath + #13#10 +
      ExpandConstant('{cm:SmartVersion}') + ' ' + SmartCurrentVersion + #13#10 +
      ExpandConstant('{cm:SmartOptionalNote}');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  FileName: String;
begin
  Result := '';
  if not SmartInstallBox.Checked then
  begin
    Log('smartmontools: existing working binary kept (no upgrade requested).');
    Exit;
  end;

  try
    ExtractTemporaryFile('{#SmartPackage}');
    FileName := ExpandConstant('{tmp}\{#SmartPackage}');
  except
    Result := 'Unable to extract bundled smartmontools installer: ' +
      GetExceptionMessage;
    Exit;
  end;

  Log('smartmontools: running verified bundled prerelease installer.');
  // Use System PATH, not the elevated administrator's User PATH.
  // Upstream installer documents /S and /SO component selection.
  if not Exec(FileName,
    '/S /SO x64,smartctl,smartd,smartctlnc,drivedb,doc,uninst,menu,syspath',
    '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
  begin
    Result := 'Bundled smartmontools installation failed (exit code ' +
      IntToStr(Code) + '). DiskChecker installation was stopped.';
    Exit;
  end;

  RefreshSmartStatus();
  if not SmartFound then
  begin
    Result := 'smartmontools installer completed but smartctl.exe is not ' +
      'usable. DiskChecker installation was stopped.';
    Exit;
  end;

  if CompareSmartVersion(SmartCurrentVersion) <> 0 then
  begin
    Result := 'smartmontools installation did not report the bundled ' +
      'version {#SmartVersion} (detected: ' + SmartCurrentVersion + '). ' +
      'DiskChecker installation was stopped.';
    Exit;
  end;
  Log('smartmontools: bundled version verified successfully.');
end;
