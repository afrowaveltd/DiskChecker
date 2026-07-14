Get-ChildItem -Path 'D:\DiskChecker' -Recurse -Filter '*.cs' | ForEach-Object {
    $lines = [System.IO.File]::ReadAllLines($_.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'enum\s+TestType') {
            $end = [Math]::Min($i + 30, $lines.Length)
            for ($j = $i; $j -lt $end; $j++) {
                Write-Output "$($_.Name):$($j+1):$($lines[$j])"
            }
        }
    }
}
