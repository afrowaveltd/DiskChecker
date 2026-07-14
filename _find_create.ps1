$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs")
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "CreateCertificate|SaveChanges|Add.*certificate") {
        $start = [Math]::Max(0, $i - 3)
        $end = [Math]::Min($lines.Length - 1, $i + 20)
        for ($j = $start; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
        Write-Host "---"
    }
}
