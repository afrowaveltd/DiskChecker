$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find all places where SelfTestTypeText, SelfTestProgress, SelfTestProgressText, IsSelfTestRunning are set
$patterns = @("SelfTestTypeText\s*=", "SelfTestProgress\s*=", "SelfTestProgressText\s*=", "IsSelfTestRunning\s*=")
foreach ($pattern in $patterns) {
    Write-Output "`n--- Pattern: $pattern ---"
    for ($i = 0; $i -lt $c.Count; $i++) {
        if ($c[$i] -match $pattern) {
            Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i].Trim())
        }
    }
}