$path = "D:\DiskChecker\DiskChecker.Application\Services\SettingsService.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content