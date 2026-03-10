$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\NavigationService.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content