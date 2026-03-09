$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit špatné proměnné (status.Value -> result.SelfTestStatus.Value)
$content = $content -replace 'status\.Value\.StatusText\(\)', 'result.SelfTestStatus.Value.StatusText()'
$content = $content -replace 'status\.Value\.GetRemainingPercent\(\)', 'result.SelfTestStatus.Value.GetRemainingPercent()'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartCheckViewModel.cs - fixed variable names"