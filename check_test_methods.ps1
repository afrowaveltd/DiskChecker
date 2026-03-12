$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Find RunShortTestAsync, RunLongTestAsync, RunSmartFeatureAsync
$startLines = @()
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task Run(Short|Long)TestAsync" -or $c[$i] -match "private async Task RunSmartFeatureAsync") {
        $startLines += $i
        Write-Output ("Found at line {0}: {1}" -f ($i+1), $c[$i].Trim())
    }
}

# Show the methods
foreach ($start in $startLines) {
    Write-Output "`n--- Method starting at line $($start+1) ---"
    $braceCount = 0
    $started = $false
    for ($i = $start; $i -lt [Math]::Min($start + 80, $c.Count); $i++) {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
        $braceCount += ($c[$i].ToCharArray() | ? {$_ -eq '{'}).Count
        $braceCount -= ($c[$i].ToCharArray() | ? {$_ -eq '}'}).Count
        if ($started -and $braceCount -eq 0) { break }
        $started = $true
    }
}