$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
for ($i = 370; $i -le 420; $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum : $($lines[$i])"
}
