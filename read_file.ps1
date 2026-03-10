$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.Application\Services\DiskDetectionService.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
$lines = $content -split "`n"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "IsSystemDisk|GetSystemDiskPath|_systemDiskPath|NormalizePath") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}