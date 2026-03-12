$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show StartPollingSelfTestProgress method
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task StartPollingSelfTestProgress") {
        Write-Output "Found at line $($i+1)"
        # Show the full method
        $braceCount = 0
        $started = $false
        for ($j = $i; $j -lt $c.Count; $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
            $braceCount += ($c[$j].ToCharArray() | ? {$_ -eq '{'}).Count
            $braceCount -= ($c[$j].ToCharArray() | ? {$_ -eq '}'}).Count
            if ($started -and $braceCount -eq 0) { break }
            $started = $true
        }
        break
    }
}