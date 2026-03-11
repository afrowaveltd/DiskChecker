$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"

Write-Output "=== Searching for SelfTestProgress, IsSelfTestRunning, and polling logic ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'SelfTestProgress|IsSelfTestRunning|StartPolling|StopPolling|_selfTestPolling|SelfTestProgressText') {
        Write-Output "$($i+1): $($lines[$i])"
    }
}