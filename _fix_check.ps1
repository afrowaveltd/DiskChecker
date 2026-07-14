$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.Collections.ArrayList]@([System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8))
Write-Output ("Current lines: " + $lines.Count)

# Let's check what happened - did the previous attempt corrupt anything?
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "ROW_NUMBER|Id %|modulo") {
        Write-Output ("Line $i : " + $lines[$i])
    }
}
