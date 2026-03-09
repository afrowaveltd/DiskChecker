$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít řádek s "public static class SmartaSelfTestStatusExtensions" a přesunout na konec
$newLines = @()
$extensionLines = @()
$inExtension = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Začátek extension třídy
    if ($line -match "^// Extension methods|^public static class SmartaSelfTestStatusExtensions") {
        $inExtension = $true
    }
    
    if ($inExtension) {
        $extensionLines += $line
        # Konec extension třídy
        if ($line -match "^}" -and $extensionLines.Count -gt 2) {
            $inExtension = $false
        }
        continue
    }
    
    $newLines += $line
}

# Přidat extension metody na úplný konec (za poslední })
$newContent = $newLines -join "`n"
$newContent = $newContent.TrimEnd()

# Přidat extension metody
$extensionContent = $extensionLines -join "`n"
$newContent = $newContent + "`n" + $extensionContent

# Uložit
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "Extension methods moved to end of file"

# Zkontrolovat výsledek
$finalLines = $newContent -split "`n"
Write-Output "Total lines: $($finalLines.Count)"
Write-Output "=== Last 20 lines ==="
for ($i = [Math]::Max(0, $finalLines.Count - 20); $i -lt $finalLines.Count; $i++) {
    Write-Output "$($i+1): $($finalLines[$i])"
}