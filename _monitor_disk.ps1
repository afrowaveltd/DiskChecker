# DiskChecker Diagnostic Monitor
# Run this script BEFORE generating the certificate
# It monitors disk free space and DB file size every second
# Press Ctrl+C to stop

param(
    [string]$DbPath = "D:\DiskChecker\DiskChecker.db",
    [int]$IntervalMs = 500
)

$host.UI.RawUI.WindowTitle = "DISK MONITOR - Running..."
Write-Host "=== DiskChecker Diagnostic Monitor ===" -ForegroundColor Green
Write-Host "Monitoring DB: $DbPath" -ForegroundColor Yellow
Write-Host "Interval: ${IntervalMs}ms" -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
Write-Host ""

$drive = (Split-Path $DbPath -Qualifier)
$lastFree = 0
$lastDbSize = 0
$startTime = Get-Date

function Get-FreeSpaceGB {
    $disk = Get-PSDrive -Name ($drive -replace ':','')
    return [math]::Round($disk.Free / 1GB, 2)
}

function Get-DbSizeMB {
    if (Test-Path $DbPath) {
        return [math]::Round((Get-Item $DbPath).Length / 1MB, 2)
    }
    return -1
}

function Get-TempDbFiles {
    $tempDir = [System.IO.Path]::GetTempPath()
    $etilqs = Get-ChildItem -Path $tempDir -Filter "etilqs*" -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
    $journal = Get-ChildItem -Path $tempDir -Filter "*.sqlite-journal" -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
    $wal = Get-ChildItem -Path $tempDir -Filter "*.sqlite-wal" -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
    $shm = Get-ChildItem -Path $tempDir -Filter "*.sqlite-shm" -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
    return @{
        EtqilsMB = [math]::Round(($etilqs.Sum) / 1MB, 2)
        JournalMB = [math]::Round(($journal.Sum) / 1MB, 2)
        WalMB = [math]::Round(($wal.Sum) / 1MB, 2)
        ShmMB = [math]::Round(($shm.Sum) / 1MB, 2)
    }
}

function Get-PageFileSizeMB {
    try {
        $pageFile = Get-CimInstance -ClassName Win32_PageFileUsage -ErrorAction SilentlyContinue
        if ($pageFile) {
            return [math]::Round($pageFile.CurrentUsage / 1MB, 0)
        }
    } catch {}
    return -1
}

Write-Host ("{0,-8} {1,-12} {2,-12} {3,-12} {4,-12} {5,-14}" -f "Time", "Free(GB)", "DB(MB)", "ΔFree(MB)", "ΔDB(MB)", "PageFile(MB)") -ForegroundColor Cyan
Write-Host ("{0,-8} {1,-12} {2,-12} {3,-12} {4,-12} {5,-14}" -f "----", "--------", "------", "--------", "-------", "-----------") -ForegroundColor Cyan

while ($true) {
    $now = Get-Date
    $elapsed = ($now - $startTime).TotalSeconds
    $free = Get-FreeSpaceGB
    $dbSize = Get-DbSizeMB
    $tempFiles = Get-TempDbFiles
    $pageFile = Get-PageFileSizeMB
    
    $deltaFree = if ($lastFree -ne 0) { [math]::Round(($lastFree - $free) * 1024, 0) } else { 0 }
    $deltaDb = if ($lastDbSize -ne 0) { [math]::Round($dbSize - $lastDbSize, 2) } else { 0 }
    
    $timeStr = "{0:D2}:{1:D2}:{2:D2}" -f $now.Hour, $now.Minute, $now.Second
    
    # Color based on disk consumption rate
    $color = "White"
    if ($deltaFree -gt 100) { $color = "Red" }
    elseif ($deltaFree -gt 10) { $color = "Yellow" }
    
    Write-Host ("{0,-8} {1,-12} {2,-12} {3,-12} {4,-12} {5,-14}" -f $timeStr, $free, $dbSize, $deltaFree, $deltaDb, $pageFile) -ForegroundColor $color
    
    # Alert on significant changes
    if ($deltaFree -gt 50) {
        Write-Host "  *** WARNING: Rapid disk consumption! Delta=${deltaFree}MB ***" -ForegroundColor Red
        Write-Host "  Temp files: etilqs=${($tempFiles.EtqilsMB)}MB, journal=${($tempFiles.JournalMB)}MB, wal=${($tempFiles.WalMB)}MB" -ForegroundColor Red
    }
    
    # Show temp files every 10 seconds
    if ([math]::Floor($elapsed) % 10 -eq 0 -and $elapsed -gt 0 -and [math]::Floor($elapsed) -ne [math]::Floor($elapsed - $IntervalMs/1000)) {
        Write-Host "  [Temp] etilqs=${($tempFiles.EtqilsMB)}MB journal=${($tempFiles.JournalMB)}MB wal=${($tempFiles.WalMB)}MB shm=${($tempFiles.ShmMB)}MB" -ForegroundColor DarkGray
    }
    
    $lastFree = $free
    $lastDbSize = $dbSize
    
    Start-Sleep -Milliseconds $IntervalMs
}
