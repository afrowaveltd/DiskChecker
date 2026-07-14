$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
for ($i = 1415; $i -le 1450; $i++) {
    if ($i -lt $lines.Count) {
        Write-Host "$($i+1): $($lines[$i])"
    }
}
