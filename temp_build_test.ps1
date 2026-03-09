Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Select-String "error" | Out-File -FilePath "D:\DiskChecker\errors2.txt" -Encoding UTF8
$errorCount = (Get-Content "D:\DiskChecker\errors2.txt" | Measure-Object -Line).Lines
Write-Output "Errors found: $errorCount"