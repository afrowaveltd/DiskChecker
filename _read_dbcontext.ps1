$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs'
$lines = Get-Content $file
$total = $lines.Count
for ($i = 0; $i -lt $total; $i++) {
    if ($lines[$i] -match "OwnsMany|OwnsOne|TestSession|WriteSamples|ReadSamples|TemperatureSamples|SmartAttribute|AutoInclude") {
        $ctx = ''
        for ($j = [Math]::Max(0, $i-2); $j -le [Math]::Min($total-1, $i+5); $j++) {
            $ctx += "$($j+1): $($lines[$j])\n"
        }
        Write-Host "--- Match at line $($i+1) ---"
        Write-Host $ctx
    }
}
Write-Host "Total lines: $total"
