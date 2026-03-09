$utf8 = New-Object System.Text.UTF8Encoding $false

# Zkopírovat QualityCalculator.cs z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Services\QualityCalculator.cs"
$destPath = "D:\DiskChecker\DiskChecker.Core\Services\QualityCalculator.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)
[System.IO.File]::WriteAllText($destPath, $content, $utf8)
Write-Output "QualityCalculator.cs restored from Previous"