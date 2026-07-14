Write-Host "===== DISK C: ====="
$drive = Get-PSDrive C
Write-Host "Free: $([math]::Round($drive.Free/1GB, 2)) GB"
Write-Host "Used: $([math]::Round($drive.Used/1GB, 2)) GB"

Write-Host "`n===== DB FILE ====="
$db = Get-Item "D:\DiskChecker\DiskChecker.UI.Avalonia\bin\Debug\net10.0\DiskChecker.db" -ErrorAction SilentlyContinue
if ($db) { Write-Host "DB Size: $([math]::Round($db.Length/1MB, 2)) MB" }
else { Write-Host "DB not found" }

Write-Host "`n===== DB WAL/SHM/JOURNAL ====="
Get-ChildItem "D:\DiskChecker\DiskChecker.UI.Avalonia\bin\Debug\net10.0" -Filter "*.db-*" | ForEach-Object {
    Write-Host "$($_.Name): $([math]::Round($_.Length/1MB, 2)) MB"
}

Write-Host "`n===== SQLITE TEMP FILES (etilqs) ====="
$etilqs = Get-ChildItem $env:TEMP -Filter "etilqs*" -ErrorAction SilentlyContinue
$count = ($etilqs | Measure-Object).Count
$sumMB = [math]::Round(($etilqs | Measure-Object -Property Length -Sum).Sum/1MB, 2)
Write-Host "Count: $count, Total: $sumMB MB"

Write-Host "`n===== PAGE FILE ====="
$pf = Get-CimInstance Win32_PageFileUsage
$pf | ForEach-Object {
    Write-Host "Name: $($_.Name)"
    Write-Host "CurrentUsage: $([math]::Round($_.CurrentUsage/1MB, 2)) MB"
    Write-Host "AllocatedBaseSize: $([math]::Round($_.AllocatedBaseSize/1MB, 2)) MB"
}

Write-Host "`n===== DISKCHECKER PROCESS ====="
Get-Process -Name "DiskChecker*" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Name: $($_.ProcessName)"
    Write-Host "WorkingSet: $([math]::Round($_.WorkingSet64/1MB, 2)) MB"
    Write-Host "PrivateMemory: $([math]::Round($_.PrivateMemorySize64/1MB, 2)) MB"
    Write-Host "VirtualMemory: $([math]::Round($_.VirtualMemorySize64/1MB, 2)) MB"
}

Write-Host "`n===== TOP 10 LARGEST FILES IN TEMP ====="
Get-ChildItem $env:TEMP -File -ErrorAction SilentlyContinue | Sort-Object Length -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host "$([math]::Round($_.Length/1MB, 2)) MB - $($_.Name)"
}
