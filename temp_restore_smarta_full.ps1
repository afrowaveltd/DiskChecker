$utf8 = New-Object System.Text.UTF8Encoding $false

# Obnovit původní SmartaData.cs z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$destPath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)
[System.IO.File]::WriteAllText($destPath, $content, $utf8)
Write-Output "SmartaData.cs restored from Previous"

# Zkontrolovat počet řádků
$lines = $content -split "`n"
Write-Output "Lines: $($lines.Count)"