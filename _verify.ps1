$f = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$c = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
$lines = $c -split "`n"
Write-Output ("Lines: " + $lines.Count)
Write-Output ("Has ROW_NUMBER: " + $c.Contains("ROW_NUMBER()"))
Write-Output ("Has modulo: " + $c.Contains("Id % @step"))
Write-Output ("Has temp_store: " + $c.Contains("temp_store=FILE"))
