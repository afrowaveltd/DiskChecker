$path = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Find all places where ConfigureSqliteReadOptimizationsAsync is called (not just defined)
Write-Output "=== ConfigureSqliteReadOptimizationsAsync CALLS ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'ConfigureSqliteReadOptimizationsAsync\(' -and $lines[$i] -notmatch 'private\s+static\s+async') {
        $start = [Math]::Max(0, $i - 3)
        $end = [Math]::Min($lines.Length - 1, $i + 3)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
        }
        Write-Output "---"
    }
}

Write-Output ""
Write-Output "=== GetTestSessionWithoutSamplesAsync method ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'GetTestSessionWithoutSamplesAsync') {
        for ($j = $i; $j -lt [Math]::Min($i + 50, $lines.Length); $j++) {
            Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
        }
        break
    }
}

Write-Output ""
Write-Output "=== GetByIdAsync method ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'public.*GetByIdAsync') {
        for ($j = $i; $j -lt [Math]::Min($i + 30, $lines.Length); $j++) {
            Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
        }
        break
    }
}
