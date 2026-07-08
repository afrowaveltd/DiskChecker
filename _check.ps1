$model = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Core\Models\DiskCertificate.cs')
$gen = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs')

# Check model
$idx = $model.IndexOf('AttributeId')
Write-Output "Model AttributeId: $idx"

$idx = $model.IndexOf('class SmartAttributeSummary')
$end = $model.IndexOf('}', $idx + 200)
Write-Output "Model class:"
Write-Output $model.Substring($idx, $end - $idx + 1)

# Check generator
$idx = $gen.IndexOf('SmartAttributes.Add')
Write-Output "Gen SmartAttributes.Add: $idx"
if ($idx -ge 0) {
    $start = [Math]::Max(0, $idx - 30)
    $len = [Math]::Min(250, $gen.Length - $start)
    Write-Output "Gen context:"
    Write-Output $gen.Substring($start, $len)
}

$idx = $gen.IndexOf('AttributeId')
Write-Output "Gen AttributeId: $idx"
