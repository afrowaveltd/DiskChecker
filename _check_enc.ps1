$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$bytes = [System.IO.File]::ReadAllBytes($file)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
Write-Host "Has BOM: $hasBom"
Write-Host "File size: $($bytes.Length)"

# Read as UTF-8 without BOM detection
$content = [System.Text.Encoding]::UTF8.GetString($bytes)
$line = $content.IndexOf("CreateCertificateAsync")
Write-Host "Found at position: $line"
if ($line -ge 0) {
    $snip = $content.Substring([Math]::Max(0, $line - 50), 400)
    # Show hex of the snippet
    $hex = [System.BitConverter]::ToString([System.Text.Encoding]::UTF8.GetBytes($snip))
    Write-Host "Hex: $hex"
}
