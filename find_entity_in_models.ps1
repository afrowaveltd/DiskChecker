# Find entity class definitions in Core/Models
$searchPath = "D:\DiskChecker\DiskChecker.Core\Models"
$files = Get-ChildItem -Path $searchPath -Recurse -Filter "*.cs" -File

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    if ($content -match 'public class (DiskCard|TestSession|DiskCertificate|DiskArchive)') {
        Write-Output "=== $($file.FullName) ==="
        # Show only class definition lines
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'public class|public int Id|SmartaData') {
                Write-Output "$($i+1): $($lines[$i])"
            }
        }
        Write-Output ""
    }
}