$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Show the method around ExportCertificateAsync call (line 370-410)
for ($i = 355; $i -lt [Math]::Min(430, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
