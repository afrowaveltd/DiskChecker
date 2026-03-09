Set-Location "D:\DiskChecker"
rd -Recurse -Force "DiskChecker.UI.Avalonia\obj", "DiskChecker.UI.WPF\obj", "DiskChecker.UI\obj" -ErrorAction SilentlyContinue
dotnet build 2>&1 | Out-File -FilePath "D:\DiskChecker\build_output3.txt" -Encoding UTF8
Write-Output "Build completed"