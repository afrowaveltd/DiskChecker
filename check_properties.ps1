$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find SelfTestTypeText, SelfTestProgress, SelfTestProgressText properties
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SelfTestTypeText|SelfTestProgress[^T]|SelfTestProgressText") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}