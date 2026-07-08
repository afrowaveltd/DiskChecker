$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$smartIdx = $c.IndexOf('SmartAttributes')
$iscritIdx = $c.IndexOf('IsCritical')

Write-Host "SmartAttributes: $smartIdx"
Write-Host "IsCritical: $iscritIdx"

# Extract SmartAttributes context
if ($smartIdx -ge 0) {
    $start = [Math]::Max(0, $smartIdx - 100)
    $len = [Math]::Min(700, $c.Length - $start)
    $context = $c.Substring($start, $len)
    [System.IO.File]::WriteAllText('D:\DiskChecker\_smartctx.txt', $context)
    Write-Host "SmartAttributes context saved"
}

# Extract IsCritical context
if ($iscritIdx -ge 0) {
    $start = [Math]::Max(0, $iscritIdx - 50)
    $len = [Math]::Min(250, $c.Length - $start)
    $context = $c.Substring($start, $len)
    [System.IO.File]::WriteAllText('D:\DiskChecker\_iscritctx.txt', $context)
    Write-Host "IsCritical context saved"
}
