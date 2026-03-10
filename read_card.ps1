$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskStatusCardItem.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content