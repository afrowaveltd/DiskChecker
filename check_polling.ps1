$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find StartPollingSelfTestProgress
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task StartPollingSelfTestProgress") {
        Write-Output "Found at line $($i+1)"
        # Show the full method (up to 150 lines)
        for ($j = $i; $j -lt [Math]::Min($i + 150, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}