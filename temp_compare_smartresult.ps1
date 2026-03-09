$utf8 = New-Object System.Text.UTF8Encoding $false

# Zkontrolovat SmartCheckResult vlastnosti v Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$prevContent = [System.IO.File]::ReadAllText($prevPath, $utf8)

# Najít vlastnosti v SmartCheckResult
Write-Output "=== SmartCheckResult in Previous ==="
$prevLines = $prevContent -split "`n"
$inSmartCheckResult = $false
for ($i = 0; $i -lt $prevLines.Count; $i++) {
    if ($prevLines[$i] -match "class SmartCheckResult|public class SmartCheckResult") {
        $inSmartCheckResult = $true
    }
    if ($inSmartCheckResult) {
        Write-Output "$($i+1): $($prevLines[$i])"
        if ($prevLines[$i] -match "^}" -and $i -gt 0) {
            break
        }
    }
}