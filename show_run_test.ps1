$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show RunSelfTestAsync method complete
Write-Output "--- RunSelfTestAsync method ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task RunSelfTestAsync") {
        for ($j = $i; $j -lt $i + 75; $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}