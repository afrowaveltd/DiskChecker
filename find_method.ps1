$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find AddSpeedSample method location
$inMethod = $false
$methodStart = -1
$methodEnd = -1
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private void AddSpeedSample") {
        $methodStart = $i
        $inMethod = $true
    }
    if ($inMethod -and $c[$i] -match "^    }" -and $i -gt $methodStart + 2) {
        $methodEnd = $i
        break
    }
}
if ($methodStart -ge 0) {
    Write-Output "AddSpeedSample method: lines $($methodStart+1) to $($methodEnd+1)"
    for ($i = $methodStart; $i -le $methodEnd + 1; $i++) {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}