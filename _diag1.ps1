$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs")
for ($i = 1375; $i -lt 1440; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
