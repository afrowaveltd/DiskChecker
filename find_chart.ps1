Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'SaveSpeedChart|SaveChartImage|SaveTemperatureChart|ChartCache' -and $lines[$i] -notmatch '///') {
            Write-Output "$($_.Name):$($i+1):$($lines[$i].Trim())"
        }
    }
}
