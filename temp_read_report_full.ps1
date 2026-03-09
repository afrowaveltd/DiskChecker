$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs", $utf8)
$lines = $content -split "`n"
# Najdeme všechny řádky obsahující SpeedSample
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "SpeedSample") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 5)
        Write-Output "--- Lines $start to $end ---"
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}