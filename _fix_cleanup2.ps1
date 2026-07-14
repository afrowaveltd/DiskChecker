$f = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.Collections.ArrayList]@([System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8))

# Fix 1: Remove old duplicate comment for LoadSpeedSeriesDownsampledAsync (lines ~744-746)
# Find "window funkce" comment that's followed by another <summary>
for ($i = 0; $i -lt $lines.Count - 3; $i++) {
    if ($lines[$i] -match "window funkce.*ROW_NUMBER" -and $lines[$i+1] -match "<summary>") {
        # Remove lines $i, $i+1 (the old comment, but $i+1 is the new comment start)
        # Actually just remove the old 3-line comment: lines i-2 to i
        Write-Output "Removing old comment at lines $($i-2)-$i"
        $lines.RemoveAt($i)  # line with "window funkce"
        $lines.RemoveAt($i-1) # line with "Načte rovnoměrně"
        $lines.RemoveAt($i-2) # line with "/// <summary>"
        break
    }
}

# Fix 2: Update the public GetSpeedSampleSeriesDownsampledAsync comment (lines ~544-550)
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "Využívá window funkci" -and $lines[$i] -match "ROW_NUMBER") {
        Write-Output "Replacing GetSpeedSampleSeriesDownsampledAsync comment at lines $($i-1)-$($i+4)"
        $lines[$i-1] = "    /// <summary>"
        $lines[$i] =   "    /// Loads evenly distributed subset of speed samples using Id-modulo"
        $lines[$i+1] = "    /// sampling. At most <paramref name=""maxPoints""/> samples per phase"
        $lines[$i+2] = "    /// are loaded into memory."
        # The 4th line should stay as "</summary>"
        break
    }
}

# Fix 3: Clean up the commented-out ConfigureSqlite call at line ~564
for ($i = 0; $i -lt $lines.Count - 1; $i++) {
    if ($lines[$i] -match "Not needed: modulo sampling avoids window functions" -and $lines[$i+1] -match "^\s*$") {
        Write-Output "Cleaning up commented ConfigureSqlite call at line $i"
        $lines.RemoveAt($i)  # remove the comment
        if ($i -lt $lines.Count -and $lines[$i] -match "^\s*$" -and $lines[$i-1] -match "^\s*\{?\s*$") {
            $lines.RemoveAt($i)  # remove blank line
        }
        break
    }
}

# Also remove other "Not needed" comments
for ($i = 0; $i -lt $lines.Count - 1; $i++) {
    if ($lines[$i] -match "^\s*// Not needed: modulo sampling avoids window functions") {
        $lines.RemoveAt($i)
        # If the next line is blank, remove it too
        if ($i -lt $lines.Count -and $lines[$i] -match "^\s*$" -and $lines[$i-1] -match "^\s*\{?\s*$") {
            $lines.RemoveAt($i)
        }
        Write-Output "Removed leftover comment at $i"
    }
}

# Write back
[System.IO.File]::WriteAllLines($f, $lines, [System.Text.Encoding]::UTF8)
Write-Output "Cleaned up! Lines: $($lines.Count)"

# Final verify
$v = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
Write-Output "Has ROW_NUMBER (in code): $($v -replace '//[^\n]*' -replace '/\*[\s\S]*?\*/' -match 'ROW_NUMBER\(\)')"
Write-Output "Has modulo: $($v.Contains('Id % @step'))"
