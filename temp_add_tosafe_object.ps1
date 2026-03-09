$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Najít řádek s "public static string ToSafeString(this int value)" a přidat verzi pro string
$lines = $content -split "`n"
$newLines = @()
$added = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $newLines += $lines[$i]
    
    # Po metodě ToSafeString(int value) přidat ToSafeString pro object/string
    if ($lines[$i] -match "public static string ToSafeString\(this int value\)" -and -not $added) {
        $newLines += ""
        $newLines += "    // Extension for any object - returns ToString or empty string"
        $newLines += "    public static string ToSafeString(this object? value)"
        $newLines += "    {"
        $newLines += "        return value?.ToString() ?? string.Empty;"
        $newLines += "    }"
        $added = $true
    }
}

# Pokud nebyl nalezen, přidat na konec souboru
if (-not $added) {
    # Najít poslední } a přidat před něj
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match "^}$") {
            # Přidat před tento řádek
            $newLines = @()
            for ($j = 0; $j -lt $i; $j++) {
                $newLines += $lines[$j]
            }
            $newLines += ""
            $newLines += "    // Extension for any object - returns ToString or empty string"
            $newLines += "    public static string ToSafeString(this object? value)"
            $newLines += "    {"
            $newLines += "        return value?.ToString() ?? string.Empty;"
            $newLines += "    }"
            for ($j = $i; $j -lt $lines.Count; $j++) {
                $newLines += $lines[$j]
            }
            break
        }
    }
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "ToSafeString extension added for object"