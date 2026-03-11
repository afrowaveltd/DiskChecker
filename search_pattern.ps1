$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"

# Find RawSmart and RefreshRaw patterns
Write-Output "=== Searching for RawSmart/RefreshRaw patterns ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'RawSmart|RefreshRaw|_rawSmart') {
        Write-Output "$($i+1): $($lines[$i].Trim())"
    }
}

Write-Output ""
Write-Output "=== Lines 1186-1221 ==="
for ($i = 1185; $i -lt 1221 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}