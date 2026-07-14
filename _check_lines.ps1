# Read the file as lines
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

# Let's first see the current state - was anything changed?
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "ROW_NUMBER|ConfigureSqlite") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 2)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j): $($lines[$j])"
        }
        Write-Output "---"
    }
}
