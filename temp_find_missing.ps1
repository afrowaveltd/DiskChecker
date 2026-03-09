$files = Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue
$types = @('interface ISettingsService', 'interface IReportService', 'class BackupInfo', 'class TestHistoryItem')
foreach ($type in $types) {
    Write-Output "=== Searching for: $type ==="
    foreach ($file in $files) {
        try {
            $content = [System.IO.File]::ReadAllText($file.FullName)
            if ($content -match [regex]::Escape($type)) {
                Write-Output "Found in: $($file.FullName)"
            }
        } catch {}
    }
}