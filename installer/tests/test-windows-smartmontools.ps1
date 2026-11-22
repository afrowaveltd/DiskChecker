# Runs on an ephemeral GitHub Actions Windows runner (administrator).
# Exercises real offline setup for (1) missing smartctl and (2) installed
# identical smartmontools. Never run this on a production workstation.
$ErrorActionPreference = 'Stop'
$Expected = '8.0-623-gd788ad8c38ee'
$Installers = @(Get-ChildItem -Path 'packages' -Filter 'DiskChecker-*-win-x64-Setup.exe')
if ($Installers.Count -ne 1) { throw "Expected exactly one DiskChecker setup EXE" }
$Setup = $Installers[0].FullName
$RegCandidates = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\smartmontools',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\smartmontools'
)
function Find-SmartmontoolsRegistryKey {
    foreach ($Candidate in $RegCandidates) {
        if (Test-Path -LiteralPath $Candidate) { return $Candidate }
    }
    return $null
}
$FirstLog = Join-Path (Get-Location) 'packages\smartmontools-first-install.log'
$SecondLog = Join-Path (Get-Location) 'packages\smartmontools-repeat-install.log'

# The runner should not have smartmontools in its machine-level installation.
$PreInstalled = $null -ne (Find-SmartmontoolsRegistryKey)
if ($PreInstalled) {
    Write-Host 'Runner has preinstalled smartmontools; mandatory-missing case will be skipped.'
}

function Invoke-DiskCheckerInstaller([string] $LogPath) {
    $Args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$LogPath")
    $Process = Start-Process -FilePath $Setup -ArgumentList $Args -Wait -PassThru
    if ($Process.ExitCode -ne 0) {
        if (Test-Path $LogPath) { Get-Content $LogPath -Tail 100 | Write-Host }
        throw "DiskChecker setup failed with exit code $($Process.ExitCode)"
    }
}

Write-Host 'First DiskChecker setup: test bundled offline smartmontools installation.'
Invoke-DiskCheckerInstaller $FirstLog
$RegPath = Find-SmartmontoolsRegistryKey
if ($null -eq $RegPath) { throw "smartmontools registry key missing after setup (both views checked)" }
$Registered = Get-ItemProperty -Path $RegPath
if ($Registered.DisplayVersion -ne $Expected) {
    throw "Expected prerelease $Expected, registered $($Registered.DisplayVersion)"
}
$Tool = Join-Path $Registered.InstallLocation 'bin\smartctl.exe'
if (-not (Test-Path $Tool)) { throw "smartctl missing from $Tool" }
& $Tool --version
if ($LASTEXITCODE -ne 0) { throw "Installed smartctl --version failed" }
if (-not $PreInstalled) {
    $Log = Get-Content -LiteralPath $FirstLog -Raw
    if (-not $Log.Contains('smartmontools: state=missing')) {
        throw "Expected mandatory missing-dependency state in first setup log"
    }
    if (-not $Log.Contains('smartmontools: bundled version verified successfully')) {
        throw "Expected post-install version verification in first setup log"
    }
}

Write-Host 'Second DiskChecker setup: same smartmontools version must be retained.'
Invoke-DiskCheckerInstaller $SecondLog
$SecondLogText = Get-Content -LiteralPath $SecondLog -Raw
if (-not $SecondLogText.Contains('smartmontools: state=exact, checkbox disabled')) {
    throw 'Expected disabled exact-version decision in second setup log'
}
if (-not $SecondLogText.Contains('smartmontools: existing working binary kept')) {
    throw 'Expected existing smartctl to be kept without reinstall'
}
if ((Get-ItemProperty $RegPath).DisplayVersion -ne $Expected) {
    throw 'Registered version changed during repeat setup'
}
Write-Host 'PASS: offline mandatory install, exact-version gray option and skip verified.'
