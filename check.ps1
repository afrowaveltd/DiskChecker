$content = [System.IO.File]::ReadAllText('D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs')
$lines = $content -split "`n"
for ($i = 85; $i -lt 110 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}