$searchPath = "D:\DiskChecker"
$files = Get-ChildItem -Path $searchPath -Recurse -Filter "DiskCertificate.cs" -ErrorAction SilentlyContinue
foreach ($f in $files) {
    Write-Output "=== $($f.FullName) ==="
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
    }
}
