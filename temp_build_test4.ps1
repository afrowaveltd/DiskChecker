Set-Location "D:\DiskChecker"
$buildOutput = dotnet build 2>&1 | Out-String
$errorLines = $buildOutput -split "`n" | Where-Object { $_ -match "error CS" }
Write-Output "Total errors: $($errorLines.Count)"
$errorLines | Select-Object -First 10