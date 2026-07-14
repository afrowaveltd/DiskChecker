$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$lines = Get-Content $file
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "TestSession" -or $lines[$i] -match "\.TestSession\s*=") {
        $ctx = ''
        for ($j = [Math]::Max(0, $i-2); $j -le [Math]::Min($lines.Count-1, $i+2); $j++) {
            $ctx += "$($j+1): $($lines[$j])\n"
        }
        Write-Host "--- Match at line $($i+1) ---"
        Write-Host $ctx
    }
}
