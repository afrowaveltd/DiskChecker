$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit volání extension metod na nullable SmartaSelfTestStatus
# 446: IsSelfTestRunning = status.IsRunning; -> status.HasValue && status.Value.IsRunning()
$content = $content -replace "IsSelfTestRunning = status\.IsRunning;", "IsSelfTestRunning = status.HasValue && status.Value.IsRunning();"

# 447: var translatedStatus = TranslateSelfTestStatus(status.StatusText);
$content = $content -replace "var translatedStatus = TranslateSelfTestStatus\(status\.StatusText\);", "var translatedStatus = TranslateSelfTestStatus(status.HasValue ? status.Value.StatusText() : \"Unknown\");"

# 448-450: SelfTestStatusText = status.RemainingPercent.HasValue...
# Nahradit celý blok
$oldBlock = "SelfTestStatusText = status\.RemainingPercent\.HasValue\s+\? \$\"\{translatedStatus\} \(zbývá \{status\.RemainingPercent\.Value\} %\)\"\s+: translatedStatus;"
$newBlock = "SelfTestStatusText = status.HasValue && status.Value.GetRemainingPercent() > 0 && status.Value.GetRemainingPercent() < 100
                ? $\"{translatedStatus} (zbývá {status.Value.GetRemainingPercent()} %)\"
                : translatedStatus;"
$content = $content -replace $oldBlock, $newBlock

# 524: IsSelfTestRunning = status.IsRunning;
$content = $content -replace "IsSelfTestRunning = status\.IsRunning;", "IsSelfTestRunning = status.HasValue && status.Value.IsRunning();"

# 529: var translatedStatus = TranslateSelfTestStatus(status.StatusText);
$content = $content -replace "var translatedStatus = TranslateSelfTestStatus\(status\.StatusText\);", "var translatedStatus = TranslateSelfTestStatus(status.HasValue ? status.Value.StatusText() : \"Unknown\");"

# 530: SelfTestStatusText = status.RemainingPercent.HasValue
$oldBlock2 = "SelfTestStatusText = status\.RemainingPercent\.HasValue\s+\? \$\"\{translatedStatus\} \(zbývá \{status\.RemainingPercent\.Value\} %\)\"\s+: translatedStatus;"
$content = $content -replace $oldBlock2, $newBlock

# 693: WarningsSummary - result.Rating.Warnings je int
$content = $content -replace "result\.Rating\.Warnings\.Count == 0", "result.Rating.Warnings == 0"

# 701: IsSelfTestRunning = result.SelfTestStatus.IsRunning;
$content = $content -replace "IsSelfTestRunning = result\.SelfTestStatus\.IsRunning;", "IsSelfTestRunning = result.SelfTestStatus.HasValue && result.SelfTestStatus.Value.IsRunning();"

# 702: var translatedStatus = TranslateSelfTestStatus(result.SelfTestStatus.StatusText);
$content = $content -replace "var translatedStatus = TranslateSelfTestStatus\(result\.SelfTestStatus\.StatusText\);", "var translatedStatus = TranslateSelfTestStatus(result.SelfTestStatus.HasValue ? result.SelfTestStatus.Value.StatusText() : \"Unknown\");"

# 703-705: SelfTestStatusText = result.SelfTestStatus.RemainingPercent.HasValue...
$oldBlock3 = "SelfTestStatusText = result\.SelfTestStatus\.RemainingPercent\.HasValue\s+\? \$\"\{translatedStatus\} \(zbývá \{result\.SelfTestStatus\.RemainingPercent\.Value\} %\)\"\s+: translatedStatus;"
$newBlock3 = "SelfTestStatusText = result.SelfTestStatus.HasValue && result.SelfTestStatus.Value.GetRemainingPercent() > 0 && result.SelfTestStatus.Value.GetRemainingPercent() < 100
                ? $\"{translatedStatus} (zbývá {result.SelfTestStatus.Value.GetRemainingPercent()} %)\"
                : translatedStatus;"
$content = $content -replace $oldBlock3, $newBlock3

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartCheckViewModel.cs updated"