$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"

Write-Output "=== StartPollingSelfTestProgress method (lines 1135-1325) ==="
for ($i = 1134; $i -lt 1325 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}