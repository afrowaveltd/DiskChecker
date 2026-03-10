$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)
$lines = $content -split "`n"

# Print entire LoadDataAsync method and related methods (lines 147-340)
for ($i = 146; $i -lt 350; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}