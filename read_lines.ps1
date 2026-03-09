param(
    [string]$File,
    [int]$StartLine,
    [int]$EndLine
)

$lines = Get-Content $File
for ($i = $StartLine - 1; $i -lt $EndLine; $i++) {
    if ($i -lt $lines.Count) {
        $lineNum = $i + 1
        Write-Host "$lineNum`: $($lines[$i])"
    }
}