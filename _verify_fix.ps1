$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'GetCertificateAsync') {
        for ($j = [Math]::Max(0, $i-1); $j -le [Math]::Min($lines.Count-1, $i+8); $j++) {
            Write-Output (($j+1).ToString() + "`t" + $lines[$j])
        }
        Write-Output "---"
    }
}
