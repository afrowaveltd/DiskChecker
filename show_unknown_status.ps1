$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show the Unknown status handling
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "else if \(status == SmartaSelfTestStatus.Unknown\)") {
        Write-Output "Found at line $($i+1)"
        for ($j = $i; $j -lt [Math]::Min($i + 50, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}