$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít řádky s duplicitními definicemi
Write-Output "=== Looking for CoreDriveInfo definition ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public class CoreDriveInfo|public record CoreDriveInfo") {
        Write-Output "$($i+1): $($lines[$i])"
        # Zobrazit okolní řádky
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($lines.Count, $i+10); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}

Write-Output ""
Write-Output "=== Looking for SmartaSelfTestReport definition ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public class SmartaSelfTestReport|public record SmartaSelfTestReport") {
        Write-Output "$($i+1): $($lines[$i])"
        # Zobrazit okolní řádky
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($lines.Count, $i+5); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}