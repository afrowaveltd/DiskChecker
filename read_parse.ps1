$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.Application\Services\DiskDetectionService.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
$lines = $content -split "`n"

# Print lines 145-175 (around the parsing logic)
for ($i = 144; $i -lt 180; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}