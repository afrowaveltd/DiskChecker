$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

# Temperature method area (~lines 600-660)
for ($i = 599; $i -lt [Math]::Min(660, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
