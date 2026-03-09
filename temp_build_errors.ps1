Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Select-String "error" | Out-File -FilePath "D:\DiskChecker\errors_only.txt" -Encoding UTF8
Write-Output "Build completed - checking errors"