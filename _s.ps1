$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$idx = $c.IndexOf('SmartAttributes')
"SmartAttributes: $idx" | Out-File 'D:\DiskChecker\_out.txt' -Encoding UTF8
$idx = $c.IndexOf('AttributeId')
"AttributeId: $idx" | Out-File 'D:\DiskChecker\_out.txt' -Append -Encoding UTF8
$idx = $c.IndexOf('Id = attr')
"Id = attr: $idx" | Out-File 'D:\DiskChecker\_out.txt' -Append -Encoding UTF8
$idx = $c.IndexOf('certificate.Smart')
"certificate.Smart: $idx" | Out-File 'D:\DiskChecker\_out.txt' -Append -Encoding UTF8

# Show context around SmartAttributes
$smartIdx = $c.IndexOf('SmartAttributes')
if ($smartIdx -ge 0) {
    $start = [Math]::Max(0, $smartIdx - 50)
    $len = [Math]::Min(300, $c.Length - $start)
    "--- SmartAttributes context ---" | Out-File 'D:\DiskChecker\_out.txt' -Append -Encoding UTF8
    $c.Substring($start, $len) | Out-File 'D:\DiskChecker\_out.txt' -Append -Encoding UTF8
}
