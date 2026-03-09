$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit volání extension metod na nullable SmartaSelfTestStatus
$content = $content -replace 'IsSelfTestRunning = status\.IsRunning;', 'IsSelfTestRunning = status.HasValue && status.Value.IsRunning();'
$content = $content -replace 'status\.StatusText', 'status.HasValue ? status.Value.StatusText() : "Unknown"'
$content = $content -replace 'status\.RemainingPercent\.HasValue', 'status.HasValue && status.Value.GetRemainingPercent() > 0'
$content = $content -replace 'status\.RemainingPercent\.Value', 'status.Value.GetRemainingPercent()'

# Opravit result.SelfTestStatus volání
$content = $content -replace 'result\.SelfTestStatus\.IsRunning', 'result.SelfTestStatus.HasValue && result.SelfTestStatus.Value.IsRunning()'
$content = $content -replace 'result\.SelfTestStatus\.StatusText', 'result.SelfTestStatus.HasValue ? result.SelfTestStatus.Value.StatusText() : "Unknown"'
$content = $content -replace 'result\.SelfTestStatus\.RemainingPercent\.HasValue', 'result.SelfTestStatus.HasValue && result.SelfTestStatus.Value.GetRemainingPercent() > 0'
$content = $content -replace 'result\.SelfTestStatus\.RemainingPercent\.Value', 'result.SelfTestStatus.Value.GetRemainingPercent()'

# Opravit Warnings - je to int, ne kolekce
$content = $content -replace 'result\.Rating\.Warnings\.Count', 'result.Rating.Warnings'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartCheckViewModel.cs updated"