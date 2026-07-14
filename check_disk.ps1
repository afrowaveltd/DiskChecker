# Check for large files and temp files
Write-Output "=== Large files in DiskChecker directory (>10MB) ==="
Get-ChildItem -Path "D:\DiskChecker" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 10MB } | Sort-Object Length -Descending | Select-Object FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}}, LastWriteTime | Format-Table -AutoSize

Write-Output ""
Write-Output "=== SQLite temp files near DB ==="
Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "*sqlite*" -ErrorAction SilentlyContinue | Select-Object FullName, Length, LastWriteTime
Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "*etilqs*" -ErrorAction SilentlyContinue | Select-Object FullName, Length, LastWriteTime

Write-Output ""
Write-Output "=== Temp directory usage ==="
$tempDir = $env:TMP
Get-ChildItem -Path $tempDir -Filter "*sqlite*" -ErrorAction SilentlyContinue | Select-Object FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}}
Get-ChildItem -Path $tempDir -Filter "etilqs*" -ErrorAction SilentlyContinue | Select-Object FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}}

Write-Output ""
Write-Output "=== Free disk space ==="
Get-PSDrive D | Select-Object Used, Free, @{N='UsedGB';E={[math]::Round($_.Used/1GB,1)}}, @{N='FreeGB';E={[math]::Round($_.Free/1GB,1)}}
