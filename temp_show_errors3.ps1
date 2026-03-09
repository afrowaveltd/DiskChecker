Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Select-String "error CS" | Out-File -FilePath "D:\DiskChecker\errors3.txt" -Encoding UTF8
Get-Content "D:\DiskChecker\errors3.txt"