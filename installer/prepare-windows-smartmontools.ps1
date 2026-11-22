# Downloads ONLY the pinned official upstream pre-release.
# The SHA256 must match GitHub's published release asset digest.
# The verified original installer is embedded in DiskChecker's own setup,
# making installation on a target machine fully offline.
$ErrorActionPreference = 'Stop'
$Version = '8.0-623-gd788ad8c38ee'
$FileName = "smartmontools-win32-setup-$Version.exe"
$ReleaseUrl = "https://github.com/smartmontools/smartmontools-builds/releases/download/smartmontools-$Version/$FileName"
$ExpectedSha256 = 'b2c77ffb58c223e8e4494442e4cd751006c00af1cfd77e834fc9f97d2fcc9f3e'
$Vendor = Join-Path $PSScriptRoot 'vendor'
$Destination = Join-Path $Vendor $FileName

New-Item -Path $Vendor -ItemType Directory -Force | Out-Null
if (Test-Path -LiteralPath $Destination) {
    $Existing = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Existing -ne $ExpectedSha256) {
        throw "Previously downloaded smartmontools checksum mismatch: $Existing"
    }
    Write-Host "smartmontools $Version is already present and verified."
    exit 0
}

try {
    Write-Host "Downloading pinned upstream smartmontools $Version..."
    Invoke-WebRequest -Uri $ReleaseUrl -OutFile $Destination -MaximumRedirection 10
    $ActualSha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ActualSha256 -ne $ExpectedSha256) {
        throw "smartmontools SHA256 mismatch. Expected $ExpectedSha256; got $ActualSha256"
    }
    Write-Host "Verified official smartmontools SHA256: $ActualSha256"
} catch {
    Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    throw
}
