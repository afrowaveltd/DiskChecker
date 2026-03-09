$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Najít a odstranit duplicitní uzavření třídy a metodu
$content = $content -replace '\}\r?\n    \r?\n    public async Task DeleteBackupAsync', "`r`n`r`n    public async Task DeleteBackupAsync"

# Ujistit se, že je správně strukturováno
# Najít konec souboru a opravit

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "BackupService.cs structure check"