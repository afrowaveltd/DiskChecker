$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find RunTestAsync method
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task RunTestAsync|private async Task RunSanitization") {
        Write-Output "Method starts at line $($i+1)"
        # Find method body
        $braceCount = 0
        $started = $false
        for ($j = $i; $j -lt [Math]::Min($i + 80, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
            $braceCount += ($c[$j].ToCharArray() | ? {$_ -eq '{'}).Count
            $braceCount -= ($c[$j].ToCharArray() | ? {$_ -eq '}'}).Count
            if ($started -and $braceCount -eq 0) { break }
            $started = $true
        }
        break
    }
}