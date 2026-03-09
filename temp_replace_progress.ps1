$utf8 = New-Object System.Text.UTF8Encoding $false

# Zkopírovat soubor z Previous a přidat using
$prevPath = "D:\DiskChecker\Previous\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.ProgressHandling.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)

# Přidat using DiskChecker.UI.WPF.Models pokud tam není
if (-not ($content.Contains("using DiskChecker.UI.WPF.Models;"))) {
    # Najít using sekci a přidat
    $content = $content -replace "(using System.Diagnostics;)", "`$1`nusing DiskChecker.UI.WPF.Models;"
}

$destPath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.ProgressHandling.cs"
[System.IO.File]::WriteAllText($destPath, $content, $utf8)
Write-Output "SurfaceTestViewModel.ProgressHandling.cs replaced from Previous"

# Zkontrolovat první řádky
$lines = $content -split "`n"
Write-Output "=== First 20 lines ==="
for ($i = 0; $i -lt 20 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}