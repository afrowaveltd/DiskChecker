$path = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Show all SQL queries (lines with SELECT, FROM, WHERE, JOIN, etc)
Write-Output "=== SQL QUERIES IN REPO ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i].Trim()
    if ($line -match '"SELECT|"SELECT|\bSELECT\b.*FROM|FROM SpeedSamples|FROM TemperatureSamples|ROW_NUMBER|GROUP BY|ORDER BY|COUNT\(\*\)|temp_store|cache_size') {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $line)
    }
}
