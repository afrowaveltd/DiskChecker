$files = Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs'
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    if ($content -match 'class SmartaAttributeItem|record SmartaAttributeItem') {
        Write-Output $file.FullName
    }
}