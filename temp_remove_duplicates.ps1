$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

$newLines = @()
$skipMode = $false
$skipStart = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Najít začátek CoreDriveInfo a přeskočit až po }
    if ($line -match "^public class CoreDriveInfo" -or $line -match "^public record CoreDriveInfo") {
        $skipMode = $true
        $skipStart = $i
        continue
    }
    
    # Najít začátek SmartaSelfTestReport a přeskočit až po }
    if ($line -match "^public class SmartaSelfTestReport" -or $line -match "^public record SmartaSelfTestReport") {
        $skipMode = $true
        $skipStart = $i
        continue
    }
    
    # Konec přeskočení
    if ($skipMode -and $line -match "^}") {
        $skipMode = $false
        # Přeskočit prázdný řádek po }
        if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match "^\s*$") {
            $i++
        }
        continue
    }
    
    if ($skipMode) {
        continue
    }
    
    $newLines += $line
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "Removed duplicate CoreDriveInfo and SmartaSelfTestReport from SmartaData.cs"
Write-Output "New line count: $($newLines.Count)"