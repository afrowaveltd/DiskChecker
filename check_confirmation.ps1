$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find ShowSelfTestConfirmationAsync
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "ShowSelfTestConfirmationAsync|SelfTestConfirmationResult") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}