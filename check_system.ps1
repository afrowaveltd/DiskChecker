# Check DB and disk state
$releaseDb = "D:\DiskChecker\DiskChecker.UI.Avalonia\bin\Release\net10.0\DiskChecker.db"
$mainDb = "D:\DiskChecker\DiskChecker.db"

Write-Output "=== DB FILES ==="
foreach ($db in @($releaseDb, $mainDb)) {
    if (Test-Path $db) {
        $info = Get-Item $db
        Write-Output "$($info.Name): $($info.Length.ToString('N0')) bytes, modified: $($info.LastWriteTime)"
    }
    # Check WAL/SHM
    foreach ($ext in @("-wal", "-shm", "-journal")) {
        $walPath = $db + $ext
        if (Test-Path $walPath) {
            $walInfo = Get-Item $walPath
            Write-Output "  $($ext): $($walInfo.Length.ToString('N0')) bytes"
        }
    }
}

Write-Output ""
Write-Output "=== CERTIFICATES DIRECTORY ==="
$certDir = "C:\Users\lo505926\AppData\Roaming\DiskChecker\Certificates"
if (Test-Path $certDir) {
    $files = Get-ChildItem $certDir | Sort-Object LastWriteTime -Descending
    foreach ($f in $files) {
        Write-Output "$($f.Name): $($f.Length.ToString('N0')) bytes, $($f.LastWriteTime)"
    }
} else {
    Write-Output "NOT FOUND"
}

Write-Output ""
Write-Output "=== CHART CACHE DIRECTORY ==="
$chartDir = "C:\Users\lo505926\AppData\Roaming\DiskChecker\ChartCache"
if (Test-Path $chartDir) {
    $files = Get-ChildItem $chartDir -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 10
    $totalSize = (Get-ChildItem $chartDir -Recurse | Measure-Object -Property Length -Sum).Sum
    Write-Output "Total files: $((Get-ChildItem $chartDir -Recurse).Count), Total size: $($totalSize.ToString('N0')) bytes"
    foreach ($f in $files) {
        Write-Output "$($f.Name): $($f.Length.ToString('N0')) bytes"
    }
} else {
    Write-Output "NOT FOUND"
}

Write-Output ""
Write-Output "=== LARGE FILES IN DISKCHECKER DIR (>10MB) ==="
Get-ChildItem -Path "D:\DiskChecker" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 10MB } | Sort-Object Length -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Output "$($_.Name): $($_.Length.ToString('N0')) bytes @ $($_.DirectoryName)"
}

Write-Output ""
Write-Output "=== TEMP FILES (etilqs) ==="
Get-ChildItem -Path "$env:TEMP" -Filter "etilqs_*" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output "$($_.Name): $($_.Length.ToString('N0')) bytes"
}
Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "etilqs_*" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output "$($_.FullName): $($_.Length.ToString('N0')) bytes"
}

Write-Output ""
Write-Output "=== FREE DISK SPACE ==="
$drive = Get-PSDrive -Name (Get-Location).Drive.Name
Write-Output "Drive $($drive.Name): $($drive.Free.ToString('N0')) free of $($drive.Used.ToString('N0') + $drive.Free.ToString('N0')) bytes"
