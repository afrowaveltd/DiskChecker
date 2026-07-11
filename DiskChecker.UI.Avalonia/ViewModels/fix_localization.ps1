$path = 'K:\Afrowave Projects\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs'
$encoding = [System.Text.Encoding]::GetEncoding(1250)
$bytes = [System.IO.File]::ReadAllBytes($path)
$text = $encoding.GetString($bytes)
$lines = $text -split "`r`n"

# Remove empty lines 1523-1535 (the old multi-line message leftovers)
$newLines = @()
for($i = 0; $i -lt $lines.Length; $i++) {
    if($i -ge 1523 -and $i -le 1535) {
        # Skip these empty lines
        continue
    }
    $newLines += $lines[$i]
}

$newText = $newLines -join "`r`n"
[System.IO.File]::WriteAllBytes($path, $encoding.GetBytes($newText))
Write-Host "Cleaned up empty lines."
