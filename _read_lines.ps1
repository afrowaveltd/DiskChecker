$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
for ($i = 1330; $i -le 1380; $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum : $($lines[$i])"
}
