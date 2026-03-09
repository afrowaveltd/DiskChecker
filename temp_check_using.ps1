$utf8 = New-Object System.Text.UTF8Encoding $false

# Check AnalysisViewModel
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\AnalysisViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== AnalysisViewModel.cs ==="
for ($i = 0; $i -lt 12 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""

# Check SettingsViewModel
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\SettingsViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== SettingsViewModel.cs ==="
for ($i = 0; $i -lt 12 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""

# Check HistoryViewModel
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\HistoryViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== HistoryViewModel.cs ==="
for ($i = 0; $i -lt 12 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""

# Check ReportViewModel
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== ReportViewModel.cs ==="
for ($i = 0; $i -lt 15 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""

# Check SurfaceTestViewModel
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== SurfaceTestViewModel.cs ==="
for ($i = 0; $i -lt 20 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}