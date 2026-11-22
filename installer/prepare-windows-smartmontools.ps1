# Downloads ONLY the pinned official upstream prerelease and its matching
# complete source tarball (GPLv2 redistribution). Rejects mismatched SHA256.
# Both files are embedded in DiskChecker setup for fully offline installation.
$ErrorActionPreference = 'Stop'
$Version = '8.0-623-gd788ad8c38ee'
$ExpectedFiles = @(
    @{
        Name = "smartmontools-win32-setup-$Version.exe"
        Sha256 = 'b2c77ffb58c223e8e4494442e4cd751006c00af1cfd77e834fc9f97d2fcc9f3e'
    },
    @{
        Name = "smartmontools-$Version.tar.gz"
        Sha256 = '9da427312bb8aee04b867b487f4cdebfbe510d9e6b16d5726daec5a71260662e'
    }
)
$Vendor = Join-Path $PSScriptRoot 'vendor'
New-Item -Path $Vendor -ItemType Directory -Force | Out-Null

foreach ($Item in $ExpectedFiles) {
    $Destination = Join-Path $Vendor $Item.Name
    $Expected = $Item.Sha256
    if (Test-Path -LiteralPath $Destination) {
        $Actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($Actual -ne $Expected) {
            throw "Previously downloaded $($Item.Name) checksum mismatch: $Actual"
        }
        Write-Host "Already present and verified: $($Item.Name)"
        continue
    }
    $Url = "https://github.com/smartmontools/smartmontools-builds/releases/download/smartmontools-$Version/$($Item.Name)"
    try {
        Write-Host "Downloading official upstream $($Item.Name)"
        Invoke-WebRequest -Uri $Url -OutFile $Destination -MaximumRedirection 10
        $Actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($Actual -ne $Expected) {
            throw "SHA256 mismatch for $($Item.Name). Expected $Expected; got $Actual"
        }
        Write-Host "Verified SHA256: $Actual"
    } catch {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw
    }
}
