$content = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [System.Text.Encoding]::UTF8)
$braceCount = 0
$inClass = $false
$classStart = -1

for ($i = 0; $i -lt $content.Count; $i++) {
    $line = $content[$i]
    
    if ($line -match 'public partial class SmartCheckViewModel') {
        $classStart = $i + 1
        $inClass = $true
        Write-Output ("Line {0}: CLASS START: {1}" -f ($i + 1), $line)
    }
    
    # Count braces
    $opens = ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
    $closes = ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
    
    $prevBraceCount = $braceCount
    $braceCount += $opens - $closes
    
    # Track when brace count returns to what it was before class
    if ($inClass -and $prevBraceCount -gt 0 -and $braceCount -eq 0) {
        Write-Output ("Line {0}: CLASS END DETECTED (brace count went to 0)" -f ($i + 1))
    }
    
    # Show significant lines
    if ($line -match '^\s*(public|private|protected|internal)\s+(static\s+)?(async\s+)?(Task|void|string|bool|int)') {
        Write-Output ("Line {0} [{1}]: {2}" -f ($i + 1), $braceCount, $line.Trim())
    }
}

Write-Output "`nFinal brace count: $braceCount"
Write-Output "Class started at line: $classStart"