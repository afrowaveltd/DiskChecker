$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs", $utf8)
$lines = $content -split "`n"
# Najdeme definici SmartaAttributeItem
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public class SmartaAttributeItem|public record SmartaAttributeItem") {
        $start = $i
        $end = [Math]::Min($lines.Count - 1, $i + 40)
        Write-Output "=== SmartaAttributeItem definition at line $($i+1) ==="
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
        break
    }
}