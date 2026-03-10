$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\Views\DiskSelectionView.axaml"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content