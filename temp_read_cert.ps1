$utf8 = New-Object System.Text.UTF8Encoding $false

# Opravit SurfaceTestCertificateDocumentBuilder.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\Services\SurfaceTestCertificateDocumentBuilder.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== Lines 185-205 ==="
for ($i = 184; $i -lt 205 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}