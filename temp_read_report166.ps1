$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs", $utf8)
$lines = $content -split "`n"
for ($i = 160; $i -lt 180 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}