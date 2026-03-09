$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Soubor používá file-scoped namespace, takže by neměl končit }
# Odstraníme poslední } pokud je navíc

$content = $content.TrimEnd()
if ($content.EndsWith("}`n}")) {
    $content = $content.Substring(0, $content.Length - 2)
    $content = $content + "`n}"
}

[System.IO.File]::WriteAllText($filePath, $content, $utf8)

# Zkontrolovat výsledek
$lines = $content -split "`n"
Write-Output "Fixed SmartaData.cs"
Write-Output "Total lines: $($lines.Count)"
Write-Output "=== Last 10 lines ==="
for ($i = [Math]::Max(0, $lines.Count - 10); $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}