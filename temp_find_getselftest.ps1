$utf8 = New-Object System.Text.UTF8Encoding $false
$files = Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue
foreach ($file in $files) {
    try {
        $content = [System.IO.File]::ReadAllText($file.FullName, $utf8)
        if ($content -match 'GetSelfTestStatusAsync') {
            Write-Output "Found in: $($file.FullName)"
        }
    } catch {}
}