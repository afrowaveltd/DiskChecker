Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Out-File -FilePath "D:\DiskChecker\current_errors.txt" -Encoding UTF8
Get-Content "D:\DiskChecker\current_errors.txt" | Select-String "error CS"