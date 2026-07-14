$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
$total = $lines.Count

Write-Host "Searching for GetTestSessionsAsync and related..."
for ($i = 0; $i -lt $total; $i++) {
    if ($lines[$i] -match "GetTestSessionsAsync|GetAllTestSessions|TestSessions.*ToList|TestSessions.*AsNoTracking|TestSessions.*Include") {
        $lineNum = $i + 1
        Write-Host "  $lineNum : $($lines[$i])"
    }
}

Write-Host ""
Write-Host "Searching for AsNoTracking usage..."
for ($i = 0; $i -lt $total; $i++) {
    if ($lines[$i] -match "AsNoTracking") {
        $lineNum = $i + 1
        Write-Host "  $lineNum : $($lines[$i])"
    }
}
