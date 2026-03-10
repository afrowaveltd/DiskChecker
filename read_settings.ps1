$path = "D:\DiskChecker\DiskChecker.Core\Interfaces\ISettingsService.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content