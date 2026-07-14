$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

# Build new array, skipping the leftover duplicate </summary> lines
# Old speed comment: indices 538-544 (7 lines) -> replace with 6 new lines at 538-543, skip 544
# Old temp comment:   indices 572-578 (7 lines) -> replace with 6 new lines at 572-577, skip 578

$newLines = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $lines.Length; $i++) {
    # Skip leftover duplicate </summary> after speed comment (original index 544)
    if ($i -eq 544) { continue }
    # Skip leftover duplicate </summary> after temperature comment (original index 578)
    if ($i -eq 578) { continue }
    
    # Replace speed comment (indices 538-543)
    if ($i -ge 538 -and $i -le 543) {
        $relIdx = $i - 538
        if ($relIdx -eq 3) {
            $newLines.Add("    /// rows where (Id % step) = 0. No window functions -- avoids temp file explosion.")
        } else {
            $speedComment = @(
                '    /// <summary>',
                '    /// Loads evenly distributed speed samples using Id-modulo sampling.',
                '    /// Calculates a step size from total count and maxPoints, then selects',
                '', # placeholder, will be set above
                '    /// Returns at most <paramref name="maxPoints"/> samples for write and read each.',
                '    /// </summary>'
            )
            $newLines.Add($speedComment[$relIdx])
        }
        continue
    }
    
    # Replace temperature comment (indices 572-577)
    if ($i -ge 572 -and $i -le 577) {
        $relIdx = $i - 572
        if ($relIdx -eq 3) {
            $newLines.Add("    /// rows where (Id % step) = 0. No window functions -- avoids temp file explosion.")
        } else {
            $tempComment = @(
                '    /// <summary>',
                '    /// Loads evenly distributed temperature samples using Id-modulo sampling.',
                '    /// Calculates a step size from total count and maxPoints, then selects',
                '', # placeholder
                '    /// Returns at most <paramref name="maxPoints"/> samples.',
                '    /// </summary>'
            )
            $newLines.Add($tempComment[$relIdx])
        }
        continue
    }
    
    # Keep all other lines
    $newLines.Add($lines[$i])
}

[System.IO.File]::WriteAllLines($path, $newLines, $encoding)
Write-Output "Fixed. New line count: $($newLines.Count) (was $($lines.Length))"
