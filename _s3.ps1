$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$idx = $c.IndexOf('SmartAttributes.Add')
Write-Output "SmartAttributes.Add: $idx"
$start = [Math]::Max(0, $idx - 200)
$len = [Math]::Min(500, $c.Length - $start)
Write-Output "--- context ---"
Write-Output $c.Substring($start, $len)
