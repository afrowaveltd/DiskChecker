$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

for ($i = 569; $i -lt [Math]::Min(582, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
