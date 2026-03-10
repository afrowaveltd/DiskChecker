# Find XAML files with vm: prefix
$files = Get-ChildItem -Path "D:\DiskChecker\DiskChecker.UI.Avalonia\Views" -Filter "*.axaml" -File

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    if ($content -match 'vm:') {
        Write-Output "=== $($file.Name) ==="
        # Show lines with vm:
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'vm:' -or $lines[$i] -match 'xmlns:vm') {
                Write-Output "$($i+1): $($lines[$i])"
            }
        }
        Write-Output ""
    }
}