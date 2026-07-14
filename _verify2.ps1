$f = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)

Write-Output "=== ROW_NUMBER occurrences ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "ROW_NUMBER") {
        $comment = $lines[$i] -match "^\s*//" -or $lines[$i - 1] -match "^\s*//"
        Write-Output ("Line $i [COMMENT=$comment]: " + $lines[$i])
    }
}

Write-Output ""
Write-Output "=== Id % @step occurrences ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "Id % @step") {
        Write-Output ("Line $i : " + $lines[$i])
    }
}

Write-Output ""
Write-Output "=== Method summary ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "(private|public) (static )?(async )?Task.*(Downsample|ConfigureSqlite)") {
        Write-Output ("Line $i : " + $lines[$i].Trim())
    }
}
