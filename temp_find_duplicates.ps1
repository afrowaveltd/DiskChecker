$utf8 = New-Object System.Text.UTF8Encoding $false

# Najít všechny soubory s CoreDriveInfo
$files = Get-ChildItem -Path 'D:\DiskChecker\DiskChecker.Core' -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, $utf8)
    if ($content -match 'class CoreDriveInfo') {
        Write-Output "Found CoreDriveInfo in: $($file.FullName)"
    }
}

# Najít všechny soubory s SmartaSelfTestReport
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, $utf8)
    if ($content -match 'class SmartaSelfTestReport') {
        Write-Output "Found SmartaSelfTestReport in: $($file.FullName)"
    }
}