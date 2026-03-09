$utf8 = New-Object System.Text.UTF8Encoding $false

# Najít GetSelfTestStatusAsync definici
$files = @(
    "D:\DiskChecker\DiskChecker.Core\Interfaces\ISmartCheckService.cs",
    "D:\DiskChecker\DiskChecker.Core\Services\SmartCheckService.cs",
    "D:\DiskChecker\DiskChecker.Infrastructure\Services\SmartCheckService.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = [System.IO.File]::ReadAllText($file, $utf8)
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "GetSelfTestStatusAsync") {
                Write-Output "=== $file ==="
                for ($j = [Math]::Max(0, $i - 2); $j -lt [Math]::Min($lines.Count, $i + 5); $j++) {
                    Write-Output "$($j+1): $($lines[$j])"
                }
                Write-Output ""
            }
        }
    }
}