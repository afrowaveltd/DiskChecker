$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Hardware\LinuxSmartaProvider.cs", [Text.Encoding]::UTF8)
# Find GetSelfTestStatusAsync method
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "GetSelfTestStatusAsync") {
        Write-Output "Found at line $($i+1)"
        # Show the method
        $braceCount = 0
        $started = $false
        for ($j = $i; $j -lt [Math]::Min($i + 100, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
            $braceCount += ($c[$j].ToCharArray() | ? {$_ -eq '{'}).Count
            $braceCount -= ($c[$j].ToCharArray() | ? {$_ -eq '}'}).Count
            if ($started -and $braceCount -eq 0) { break }
            $started = $true
        }
        break
    }
}