$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find RefreshRawOutput method
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task RefreshRawOutput") {
        Write-Output "Found at line $($i+1)"
        for ($j = $i; $j -lt [Math]::Min($i + 30, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}