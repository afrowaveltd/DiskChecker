[System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs") | ForEach-Object { $num = 1 } { Write-Host "$num : $_"; $num++ }
