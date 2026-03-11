$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"

Write-Output "=== RefreshRawOutput method (lines 923 onwards) ==="
for ($i = 922; $i -lt 1000 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== GetRawSmartctlOutputAsync method (lines 367 onwards) ==="
for ($i = 366; $i -lt 430 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}