$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs", $utf8)
$lines = $content -split "`n"

# Najít definici SelfTestStatus a dalších vlastností
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "SelfTestStatus|IsSelfTestRunning|SelfTestStatusText") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}