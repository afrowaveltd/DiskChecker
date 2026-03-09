Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Select-String "error CS"