$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\DiskSelectionView.axaml", $utf8)
Write-Output $content