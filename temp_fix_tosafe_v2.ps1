$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$lines = [System.IO.File]::ReadAllLines($filePath, $utf8)

# Najít problematické řádky (338-347) a opravit
$newLines = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    $lineNum = $i + 1
    
    if ($lineNum -eq 338) {
        # Řádek 338: public static string ToSafeString(this int value)
        # Chybí { - přidáme ji na další řádek
        $newLines += $lines[$i]
        $newLines += "    {"
    }
    elseif ($lineNum -eq 339) {
        # Řádek 339 je prázdný - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 340) {
        # Komentář - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 341) {
        # public static string ToSafeString(this object? value)
        # - přeskakujeme (je duplicitní)
        continue
    }
    elseif ($lineNum -eq 342) {
        # { - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 343) {
        # return value?.ToString() ?? string.Empty;
        # - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 344) {
        # } - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 345) {
        # { - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 346) {
        # return value.ToString();
        # - přeskakujeme
        continue
    }
    elseif ($lineNum -eq 347) {
        # } - přeskakujeme
        # Místo toho přidáme správné metody
        $newLines += "        return value.ToString();"
        $newLines += "    }"
        $newLines += "    "
        $newLines += "    // Extension for any object - returns ToString or empty string"
        $newLines += "    public static string ToSafeString(this object? value)"
        $newLines += "    {"
        $newLines += "        return value?.ToString() ?? string.Empty;"
        $newLines += "    }"
    }
    else {
        $newLines += $lines[$i]
    }
}

[System.IO.File]::WriteAllLines($filePath, $newLines, $utf8)
Write-Output "Fixed ToSafeString methods"

# Zkontroluj výsledek
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines2 = $content -split "`n"
Write-Output "=== Lines 330-355 ==="
for ($i = 329; $i -lt 355 -and $i -lt $lines2.Count; $i++) {
    Write-Output "$($i+1): $($lines2[$i])"
}