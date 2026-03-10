# Find class definitions in Infrastructure project
$searchPath = "D:\DiskChecker\DiskChecker.Infrastructure"
$files = Get-ChildItem -Path $searchPath -Recurse -Filter "*.cs" -File

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    if ($content -match 'public class (DiskCard|TestSession|DiskCertificate|DiskArchive)') {
        Write-Output "=== $($file.FullName) ==="
        Write-Output $content
        Write-Output ""
    }
}