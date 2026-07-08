$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$genIdx = $c.IndexOf('GenerateCertificateAsync')
Write-Host "GenerateCertificateAsync: $genIdx"
$smartIdx = $c.IndexOf('SmartAttributes')
Write-Host "SmartAttributes: $smartIdx"
$iscritIdx = $c.IndexOf('IsCritical')
Write-Host "IsCritical: $iscritIdx"

# Show the method
if ($genIdx -ge 0) {
    $next = $c.IndexOf('//', $genIdx + 100)
    $next2 = $c.IndexOf('//', $next + 1)
    $next3 = $c.IndexOf('//', $next2 + 1)
    $next4 = $c.IndexOf('//', $next3 + 1)
    $next5 = $c.IndexOf('//', $next4 + 1)
    $next6 = $c.IndexOf('//', $next5 + 1)
    $next7 = $c.IndexOf('//', $next6 + 1)
    $next8 = $c.IndexOf('//', $next7 + 1)
    $next9 = $c.IndexOf('//', $next8 + 1)
    $next10 = $c.IndexOf('//', $next9 + 1)
    $end = [Math]::Min($next10, $genIdx + 5000)
    $method = $c.Substring($genIdx, $end - $genIdx)
    [System.IO.File]::WriteAllText('D:\DiskChecker\_genmethod.txt', $method)
    Write-Host "Method saved: $($method.Length) chars"
}
