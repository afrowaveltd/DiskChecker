$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs", $utf8)
$lines = $content -split "`n"
# Najdeme SmartaAttributeItem
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "SmartaAttributeItem") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 30)
        Write-Output "=== Lines $($start+1) to $($end+1) ==="
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
        break
    }
}