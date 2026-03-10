$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\App.axaml.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content