$path = "D:\DiskChecker\DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content