$utf8 = New-Object System.Text.UTF8Encoding $false
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$destPath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"

# Zkopírovat původní soubor
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)
[System.IO.File]::WriteAllText($destPath, $content, $utf8)
Write-Output "SmartaData.cs restored from Previous"

# Zkontroluj počet řádek
$lines = $content -split "`n"
Write-Output "Total lines: $($lines.Count)"