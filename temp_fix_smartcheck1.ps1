$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# 1. Opravit Rating.Warnings.Count - Warnings je int
$content = $content -replace "result\.Rating\.Warnings\.Count", "result.Rating.Warnings"

# 2. Opravit status.IsRunning atd. - volá se na nullable SmartaSelfTestStatus
# Řádek 446: IsSelfTestRunning = status.IsRunning;
$content = $content -replace "IsSelfTestRunning = status\.IsRunning;", "IsSelfTestRunning = status.Value.IsRunning();"

# Řádek 447-450: status.StatusText a status.RemainingPercent
$content = $content -replace "var translatedStatus = TranslateSelfTestStatus\(status\.StatusText\);", "var translatedStatus = TranslateSelfTestStatus(status.Value.StatusText());"
$content = $content -replace "SelfTestStatusText = status\.RemainingPercent\.HasValue", "SelfTestStatusText = status.Value.GetRemainingPercent() >= 0"
$content = $content -replace "\? \$\"\{translatedStatus\} \(zbývá \{status\.RemainingPercent\.Value\} %\)\"", $": $\"{translatedStatus}\""
$content = $content -replace ": translatedStatus;", ""

# Řádek 701-705: result.SelfTestStatus.IsRunning atd.
$content = $content -replace "IsSelfTestRunning = result\.SelfTestStatus\.IsRunning;", "IsSelfTestRunning = result.SelfTestStatus.Value.IsRunning();"
$content = $content -replace "var translatedStatus = TranslateSelfTestStatus\(result\.SelfTestStatus\.StatusText\);", "var translatedStatus = TranslateSelfTestStatus(result.SelfTestStatus.Value.StatusText());"
$content = $content -replace "result\.SelfTestStatus\.RemainingPercent\.HasValue", "result.SelfTestStatus.Value.GetRemainingPercent() >= 0"
$content = $content -replace "\? \$\"\{translatedStatus\} \(zbývá \{result\.SelfTestStatus\.RemainingPercent\.Value\} %\)", ""
$content = $content -replace ": translatedStatus;", ";"

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartCheckViewModel.cs partially fixed"