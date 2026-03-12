$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SmartCheckView.axaml", [Text.Encoding]::UTF8)
# Find Self-test progress indicator
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SelfTestProgress|IsSelfTestRunning|SelfTestProgressText") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}