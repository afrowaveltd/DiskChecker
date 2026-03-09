Set-Location "D:\DiskChecker"
dotnet build 2>&1 | Out-File -FilePath "D:\DiskChecker\build_errors.txt" -Encoding UTF8
$errors = Select-String -Path "D:\DiskChecker\build_errors.txt" -Pattern "error CS"
Write-Output "Total errors: $($errors.Count)"
$errors | Select-Object -First 30