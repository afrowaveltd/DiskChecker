$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

# --- Fix 1: Replace lines 539-545 (0-based 538-544) ---
$newComment1 = @(
    '    /// <summary>',
    '    /// Loads evenly distributed speed samples using Id-modulo sampling.',
    '    /// Calculates a step size from total count and maxPoints, then selects',
    "    /// rows where (Id % step) = 0. No window functions ${([char]0x2014)} avoids temp file explosion.",
    '    /// Returns at most <paramref name="maxPoints"/> samples for write and read each.',
    '    /// </summary>'
)
for ($i = 0; $i -lt $newComment1.Length; $i++) {
    $lines[538 + $i] = $newComment1[$i]
}

# --- Fix 2: Replace lines 573-579 (0-based 572-578) ---
$newComment2 = @(
    '    /// <summary>',
    '    /// Loads evenly distributed temperature samples using Id-modulo sampling.',
    '    /// Calculates a step size from total count and maxPoints, then selects',
    "    /// rows where (Id % step) = 0. No window functions ${([char]0x2014)} avoids temp file explosion.",
    '    /// Returns at most <paramref name="maxPoints"/> samples.',
    '    /// </summary>'
)
for ($i = 0; $i -lt $newComment2.Length; $i++) {
    $lines[572 + $i] = $newComment2[$i]
}

[System.IO.File]::WriteAllLines($path, $lines, $encoding)
Write-Output "Done."
