$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.Application\Services\DiskDetectionService.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
$lines = $content -split "`n"

# Find and print GetSystemDiskPathAsync method (lines 129+)
$inMethod = $false
$braceCount = 0
for ($i = 128; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    Write-Output "$($i+1): $line"
}

Write-Output "=== IsSystemDisk method ==="
for ($i = 95; $i -lt 128; $i++) {
    $line = $lines[$i]
    Write-Output "$($i+1): $line"
}