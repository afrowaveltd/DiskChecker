$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\HistoryView.axaml", $utf8)
Write-Output $content.Substring(0, [Math]::Min(2000, $content.Length))