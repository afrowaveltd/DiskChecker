$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\Previous\DiskChecker.UI.WPF\Views\DiskSelectionView.xaml", $utf8)
Write-Output $content