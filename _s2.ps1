$c = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')
$idx = $c.IndexOf('SmartAttributes.Add')
Write-Output "SmartAttributes.Add: $idx"
$idx = $c.IndexOf('SmartAttributeSummary')
Write-Output "SmartAttributeSummary: $idx"
$idx = $c.IndexOf('IsCriticalAttribute')
Write-Output "IsCriticalAttribute: $idx"

# Show context around SmartAttributes.Add
$addIdx = $c.IndexOf('SmartAttributes.Add')
if ($addIdx -ge 0) {
    $start = [Math]::Max(0, $addIdx - 50)
    $len = [Math]::Min(400, $c.Length - $start)
    Write-Output "--- SmartAttributes.Add context ---"
    Write-Output $c.Substring($start, $len)
}

# Show context around IsCriticalAttribute
$iscritIdx = $c.IndexOf('IsCriticalAttribute')
if ($iscritIdx -ge 0) {
    $start = [Math]::Max(0, $iscritIdx - 50)
    $len = [Math]::Min(300, $c.Length - $start)
    Write-Output "--- IsCriticalAttribute context ---"
    Write-Output $c.Substring($start, $len)
}
