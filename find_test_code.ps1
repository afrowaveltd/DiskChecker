$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find RunTestAsync
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "RunTestAsync" -and $c[$i] -notmatch "RunSanitization") {
        Write-Output "Found at line $($i+1)"
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($i + 50, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}
# Find _currentPhase assignment
Write-Output "`n--- _currentPhase assignments ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "_currentPhase\s*=") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}