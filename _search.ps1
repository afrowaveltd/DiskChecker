$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$idx = $c.IndexOf('SmartAttributes')
"SmartAttributes: $idx" | Out-File 'D:\DiskChecker\_result.txt' -Encoding UTF8
$idx = $c.IndexOf('IsCritical')
"IsCritical: $idx" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
$idx = $c.IndexOf('certificate.Smart')
"certificate.Smart: $idx" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
$idx = $c.IndexOf('SmartAttributeSummary')
"SmartAttributeSummary: $idx" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
$idx = $c.IndexOf('attr.Id')
"attr.Id: $idx" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8

# Show context around SmartAttributes
$smartIdx = $c.IndexOf('SmartAttributes')
if ($smartIdx -ge 0) {
    $start = [Math]::Max(0, $smartIdx - 100)
    $len = [Math]::Min(600, $c.Length - $start)
    "--- SmartAttributes context ---" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
    $c.Substring($start, $len) | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
}

# Show context around IsCritical
$iscritIdx = $c.IndexOf('IsCritical')
if ($iscritIdx -ge 0) {
    $start = [Math]::Max(0, $iscritIdx - 50)
    $len = [Math]::Min(300, $c.Length - $start)
    "--- IsCritical context ---" | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
    $c.Substring($start, $len) | Out-File 'D:\DiskChecker\_result.txt' -Append -Encoding UTF8
}
