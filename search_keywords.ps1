Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'GetTemperatureSampleSeriesAsync[^D]|CertWidth|CertHeight|BuildImagePdf|beginDoc|\.pdf' -and $lines[$i] -notmatch '\.pdf\)|\.pdf\s*$|\\\.pdf|pdfPath|PdfPath|GeneratePdfAsync') {
            Write-Output "$($_.Name):$($i+1):$($lines[$i].Trim())"
        }
    }
}
