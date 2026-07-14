$path = "D:\DiskChecker\DiskChecker.Application\Services\TestAnalysisDataService.cs"
if (Test-Path $path) {
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
    }
} else {
    Write-Output "FILE NOT FOUND: $path"
}
