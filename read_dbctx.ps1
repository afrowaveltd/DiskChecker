$path = "D:\DiskChecker\DiskChecker.Infrastructure.Persistence\DiskCheckerDbContext.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content