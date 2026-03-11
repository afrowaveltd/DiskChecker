$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"

Write-Output "=== Lines 1239-1280 (Unknown status handling) ==="
for ($i = 1238; $i -lt 1280 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}