
$file = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs"
$lines = Get-Content $file -Encoding UTF8

# Find LoadCertificateGraphSamplesProgressiveAsync
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "LoadCertificateGraphSamplesProgressiveAsync") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 60)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j): $($lines[$j])"
        }
        Write-Output "=== MATCH ==="
    }
}
