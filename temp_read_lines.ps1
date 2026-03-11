$lines = [System.IO.File]::ReadAllLines("E:\C#\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [System.Text.Encoding]::UTF8)
$start = 1135
$end = 1250
for ($i = $start; $i -lt $end -and $i -lt $lines.Count; $i++) {
    $lineNum = $i + 1
    Write-Output "${lineNum}: $($lines[$i])"
}