$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs", $utf8)
$lines = $content -split "`n"
# Najdeme všechny řádky obsahující CreateSpeedPlot nebo SpeedSample
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "CreateSpeedPlot|SpeedSample|SpeedPlotModel") {
        $start = [Math]::Max(0, $i - 1)
        $end = [Math]::Min($lines.Count - 1, $i + 1)
        Write-Output "$($i+1): $($lines[$i])"
    }
}