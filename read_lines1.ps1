$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

# Print lines 530-580 (1-indexed)
for ($i = 529; $i -lt [Math]::Min(580, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
