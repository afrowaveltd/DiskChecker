$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show the Unknown status handling section
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "else if \(status == SmartaSelfTestStatus.Unknown\)") {
        for ($j = $i; $j -lt [Math]::Min($i + 70, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}