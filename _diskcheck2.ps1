Write-Host "===== D: DRIVE ====="
$d = Get-PSDrive D
Write-Host "Free: $([math]::Round($d.Free/1GB, 2)) GB"
Write-Host "Used: $([math]::Round($d.Used/1GB, 2)) GB"

Write-Host "`n===== PAGEFILE.SYS ACTUAL SIZE ====="
$pf = Get-Item "C:\pagefile.sys" -Force -ErrorAction SilentlyContinue
if ($pf) { Write-Host "pagefile.sys: $([math]::Round($pf.Length/1GB, 2)) GB" }
else { Write-Host "pagefile.sys not found or inaccessible" }

Write-Host "`n===== SWAPFILE.SYS ====="
$sf = Get-Item "C:\swapfile.sys" -Force -ErrorAction SilentlyContinue
if ($sf) { Write-Host "swapfile.sys: $([math]::Round($sf.Length/1GB, 2)) GB" }

Write-Host "`n===== HIBERFIL.SYS ====="
$hf = Get-Item "C:\hiberfil.sys" -Force -ErrorAction SilentlyContinue
if ($hf) { Write-Host "hiberfil.sys: $([math]::Round($hf.Length/1GB, 2)) GB" }

Write-Host "`n===== PDF FILES IN OUTPUT DIR ====="
Get-ChildItem "D:\DiskChecker" -Recurse -Filter "*.pdf" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "$([math]::Round($_.Length/1MB, 2)) MB - $($_.FullName)"
}

Write-Host "`n===== COMMITTED MEMORY (via Get-Process) ====="
$proc = Get-Process -Name "DiskChecker*" -ErrorAction SilentlyContinue
$proc | ForEach-Object {
    Write-Host "PagedMemory: $([math]::Round($_.PagedMemorySize64/1MB, 2)) MB"
    Write-Host "NonPagedMemory: $([math]::Round($_.NonpagedSystemMemorySize64/1MB, 2)) MB"
    Write-Host "PeakWorkingSet: $([math]::Round($_.PeakWorkingSet64/1MB, 2)) MB"
    Write-Host "PeakVirtualMemory: $([math]::Round($_.PeakVirtualMemorySize64/1GB, 2)) GB"
}

Write-Host "`n===== LARGEST FOLDERS IN D:\\DiskChecker ====="
Get-ChildItem "D:\DiskChecker" -Directory | ForEach-Object {
    $size = (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    Write-Host "$([math]::Round($size/1MB, 2)) MB - $($_.Name)"
}

Write-Host "`n===== MEMORY COMMIT (WMIC) ====="
$os = Get-CimInstance Win32_OperatingSystem
Write-Host "TotalVisibleMemory: $([math]::Round($os.TotalVisibleMemorySize/1MB, 2)) MB"
Write-Host "FreePhysicalMemory: $([math]::Round($os.FreePhysicalMemory/1MB, 2)) MB"
Write-Host "TotalVirtualMemory: $([math]::Round($os.TotalVirtualMemorySize/1MB, 2)) MB"
Write-Host "FreeVirtualMemory: $([math]::Round($os.FreeVirtualMemory/1MB, 2)) MB"
