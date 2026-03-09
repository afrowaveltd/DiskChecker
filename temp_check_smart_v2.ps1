$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit rozbité nahrazení - "result.SelfTeststatus" a "status" by mělo být "result.SelfTestStatus"
$content = $content -replace 'result\.SelfTeststatus', 'result.SelfTestStatus'

# Opravit řádky kolem 701-704 kde se používá string.HasValue a string.Value
# Tyto řádky byly omylem nahrazeny špatně

$lines = $content -split "`n"
Write-Output "=== Lines 698-710 ==="
for ($i = 697; $i -lt 710 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}