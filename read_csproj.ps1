$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\DiskChecker.UI.Avalonia.csproj")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
Write-Output $content