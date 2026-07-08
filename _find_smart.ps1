$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$idx = $c.IndexOf('SmartAttributes.Add')
if ($idx -ge 0) {
    $start = [Math]::Max(0, $idx - 50)
    $len = [Math]::Min(300, $c.Length - $start)
    $c.Substring($start, $len) | Out-File 'D:\DiskChecker\_smart_add.txt' -Encoding UTF8
    Write-Host "Found at $idx"
} else {
    Write-Host "NOT FOUND"
}
