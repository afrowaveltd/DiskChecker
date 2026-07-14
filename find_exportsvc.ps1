Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'ExportCertificateAsync|_exportService\.Export|CertificateExportService' -and $lines[$i] -notmatch '///') {
            $start = [Math]::Max(0, $i - 8)
            $end = [Math]::Min($lines.Length - 1, $i + 8)
            for ($j = $start; $j -le $end; $j++) {
                Write-Output "$($_.Name):$($j+1):$($lines[$j].Trim())"
            }
            Write-Output "---"
        }
    }
}
