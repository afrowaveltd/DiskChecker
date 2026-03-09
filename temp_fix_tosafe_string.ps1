$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Odstranit špatně přidanou metodu a přidat správně
$lines = $content -split "`n"
$newLines = @()
$skipUntil = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $lineNum = $i + 1
    
    # Přeskakovat problematické řádky 340-347
    if ($lineNum -ge 340 -and $lineNum -le 347) {
        continue
    }
    
    # Po ř�ádku 341 (konec ToSafeString pro int) přidat další přetížení
    if ($lineNum -eq 341) {
        $newLines += $line
        $newLines += "    "
        $newLines += "    // Extension for string - returns the string or empty"
        $newLines += "    public static string ToSafeString(this string? value)"
        $newLines += "    {"
        $newLines += "        return value ?? string.Empty;"
        $newLines += "    }"
        continue
    }
    
    $newLines += $line
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "Added ToSafeString for string"

# Zkontroluj výsledek
$content2 = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines2 = $content2 -split "`n"
Write-Output "=== Lines 330-360 ==="
for ($i = 329; $i -lt 360 -and $i -lt $lines2.Count; $i++) {
    Write-Output "$($i+1): $($lines2[$i])"
}