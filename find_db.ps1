Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "*.db" -ErrorAction SilentlyContinue | ForEach-Object { Write-Output "$($_.FullName) | $($_.Length) | $($_.LastWriteTime)" }
