
$file = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs"
$lines = Get-Content $file -Encoding UTF8

# Find lines with ExportCertificate, GeneratePdf, or EnsureChartImage or GetTestSession
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "(ExportCertificate|GeneratePdf|EnsureChartImage|GenerateChart|GetTestSession|_certificateExport|_certificateGenerator)") {
        $start = [Math]::Max(0, $i - 3)
        $end = [Math]::Min($lines.Count - 1, $i + 10)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j): $($lines[$j])"
        }
        Write-Output "---"
    }
}
