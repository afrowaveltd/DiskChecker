$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

Write-Output "=== Lines 539-546 (Speed downsampling comment) ==="
for ($i = 538; $i -lt [Math]::Min(546, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}

Write-Output ""
Write-Output "=== Lines 573-579 (Temperature downsampling comment) ==="
for ($i = 572; $i -lt [Math]::Min(579, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}

Write-Output ""
Write-Output "=== Checking for any remaining ROW_NUMBER() in code (not comments) ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line -match 'ROW_NUMBER' -and $line -notmatch '^\s*///') {
        Write-Output ('WARNING: ROW_NUMBER at line {0}: {1}' -f ($i+1), $line.Trim())
    }
}

Write-Output ""
Write-Output "=== Checking for remaining ConfigureSqliteReadOptimizationsAsync calls ==="
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line -match 'ConfigureSqlite' -and $line -notmatch '^\s*//' -and $line -notmatch '^\s*///') {
        Write-Output ('FOUND at line {0}: {1}' -f ($i+1), $line.Trim())
    }
}
Write-Output "(uncommented calls shown above)"
