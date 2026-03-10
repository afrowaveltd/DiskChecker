$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
$lines = $content -split "`n"

# Print LoadDataAsync method (around lines 140-220)
for ($i = 139; $i -lt 225; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}