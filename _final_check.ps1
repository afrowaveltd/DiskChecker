$f = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$l = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
Write-Output "Total lines: $($l.Count)"

# Check method structure
Write-Output ""
Write-Output "=== Method signatures ==="
for ($i = 0; $i -lt $l.Count; $i++) {
    if ($l[$i] -match "(private|public) (static )?(async )?Task.*(Downsample|ConfigureSqlite)") {
        Write-Output ("Line $i : " + $l[$i].Trim())
    }
}

# Check for any remaining SQL window functions
Write-Output ""
Write-Output "=== SQL window function check ==="
for ($i = 0; $i -lt $l.Count; $i++) {
    if ($l[$i] -match "ROW_NUMBER|COUNT\(\*\) OVER") {
        $inComment = $false
        for ($j = $i; $j -ge [Math]::Max(0, $i-3); $j--) {
            if ($l[$j] -match "^\s*//" -or $l[$j] -match "^\s*///") { $inComment = $true; break }
        }
        if (-not $inComment) {
            Write-Output ("WARNING - Line $i : " + $l[$i])
        }
    }
}
Write-Output "Check complete"

# Check for any remaining ConfigureSqliteReadOptimizationsAsync calls
Write-Output ""
Write-Output "=== ConfigureSqlite calls ==="
$found = $false
for ($i = 0; $i -lt $l.Count; $i++) {
    if ($l[$i] -match "ConfigureSqliteReadOptimizationsAsync\(" -and $l[$i] -notmatch "^\s*//" -and $l[$i] -notmatch "^.*private.*Task") {
        Write-Output ("Line $i : " + $l[$i].Trim())
        $found = $true
    }
}
if (-not $found) { Write-Output "None (all cleaned)" }
