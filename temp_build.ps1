Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Out-File -FilePath "D:\DiskChecker\build_output.txt" -Encoding UTF8
Write-Output "Build output saved to D:\DiskChecker\build_output.txt"