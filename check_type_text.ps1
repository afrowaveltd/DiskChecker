$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find where SelfTestTypeText is set
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SelfTestTypeText\s*=") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}

# Also find ShowSelfTestConfirmationAsync method
Write-Output "`n--- ShowSelfTestConfirmationAsync method ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task<SelfTestConfirmationResult> ShowSelfTestConfirmationAsync") {
        for ($j = $i; $j -lt $i + 30; $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}