$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs")
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "ExportPdf|GenerateNewCert|SaveChanges|Add.*cert|anomal|UpdateAsync|SaveCert") {
        $start = [Math]::Max(0, $i - 5)
        $end = [Math]::Min($lines.Length - 1, $i + 15)
        for ($j = $start; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
        Write-Host "---"
    }
}
