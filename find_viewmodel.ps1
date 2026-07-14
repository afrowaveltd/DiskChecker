Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter 'CertificateViewModel.cs' | ForEach-Object {
    Write-Output "=== $($_.FullName) ==="
    $lines = [System.IO.File]::ReadAllLines($_.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'Export.*Async|ExportCertificate|Generate.*Certif|\.Export|ExportResult|pdfPath|ChartCache|SaveChart') {
            # Show surrounding context
            $start = [Math]::Max(0, $i - 5)
            $end = [Math]::Min($lines.Length - 1, $i + 5)
            for ($j = $start; $j -le $end; $j++) {
                Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
            }
            Write-Output "---"
        }
    }
}
