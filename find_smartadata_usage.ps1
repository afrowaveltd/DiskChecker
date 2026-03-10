# Find all classes that might use SmarData as property
$files = Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "*.cs" -File

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match 'SmartaData' -and $file.Name -ne "SmartaData.cs") {
        Write-Output "=== $($file.FullName) ==="
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'SmartaData') {
                Write-Output "$($i+1): $($lines[$i])"
            }
        }
        Write-Output ""
    }
}