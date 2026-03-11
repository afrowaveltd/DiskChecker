$lines = [System.IO.File]::ReadAllLines("E:\C#\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [System.Text.Encoding]::UTF8)
$startLine = $args[0]
if ($startLine -eq $null) { $startLine = 1100 }
$endLine = $args[1]
if ($endLine -eq $null) { $endLine = 1200 }
for ($i = $startLine; $i -lt $endLine -and $i -lt $lines.Count; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}