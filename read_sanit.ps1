$path = "D:\DiskChecker\DiskChecker.Infrastructure\Hardware\Sanitization\Win32DiskInterop.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content