$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs", [System.Text.Encoding]::UTF8)
for ($i = 740; $i -lt 770; $i++) {
    Write-Output ("$i : " + $lines[$i])
}
Write-Output "---"
for ($i = 540; $i -lt 570; $i++) {
    Write-Output ("$i : " + $lines[$i])
}
