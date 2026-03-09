# Fix CertificateGenerator.cs type conversion errors

$filePath = 'D:\DiskChecker\DiskChecker.Core\Services\CertificateGenerator.cs'
Write-Host 'Reading CertificateGenerator.cs...'

$content = Get-Content -Path $filePath -Raw
$lines = $content -split '\r?\n'

Write-Host ('CertificateGenerator.cs has ' + $lines.Count + ' lines')

if ($lines.Count -gt 85) {
    Write-Host ''
    Write-Host 'Current problematic lines:'
    Write-Host 'Line 84 (index 83):' $lines[83]
    Write-Host 'Line 85 (index 84):' $lines[84]
    
    # Detect problematic patterns
    $line84HasProblem = $lines[83] -match 'ModelFamily.*\?\?'
    $line85HasProblem = $lines[84] -match 'DeviceModel.*\?\?'
    
    Write-Host ''
    Write-Host 'Pattern detection:'
    Write-Host '  Line 84 contains ModelFamily with ?? operator:' $line84HasProblem
    Write-Host '  Line 85 contains DeviceModel with ?? operator:' $line85HasProblem
    
    $fixed = $false
    
    # Apply fixes for ModelFamily pattern
    if ($line84HasProblem) {
        $original = $lines[83]
        # Fix pattern: smartaData.ModelFamily ?? "Unknown" -> smartaData.ModelFamily?.ToString() ?? "Unknown"
        $fixedLine = $original -replace 'smartaData\.ModelFamily\s*\?\?\s*"([^"]*)"', 'smartaData.ModelFamily?.ToString() ?? "$1"'
        if ($fixedLine -ne $original) {
            $lines[83] = $fixedLine
            $fixed = $true
            Write-Host ''
            Write-Host 'FIXED line 84:'
            Write-Host '  OLD: ' $original
            Write-Host '  NEW: ' $fixedLine
        }
    }
    
    # Apply fixes for DeviceModel pattern
    if ($line85HasProblem) {
        $original = $lines[84]
        # Fix pattern: smartaData.DeviceModel ?? "Unknown" -> smartaData.DeviceModel?.ToString() ?? "Unknown"
        $fixedLine = $original -replace 'smartaData\.DeviceModel\s*\?\?\s*"([^"]*)"', 'smartaData.DeviceModel?.ToString() ?? "$1"'
        if ($fixedLine -ne $original) {
            $lines[84] = $fixedLine
            $fixed = $true
            Write-Host ''
            Write-Host 'FIXED line 85:'
            Write-Host '  OLD: ' $original
            Write-Host '  NEW: ' $fixedLine
        }
    }
    
    # Write back the fixed content
    if ($fixed) {
        $newContent = $lines -join '\r\n'
        Set-Content -Path $filePath -Value $newContent -Encoding UTF8
        Write-Host ''
        Write-Host 'SUCCESS: CertificateGenerator.cs has been updated successfully!'
    } else {
        Write-Host ''
        Write-Host 'INFO: No changes were needed - patterns may already be correct'
    }
} else {
    Write-Host ''
    Write-Host 'ERROR: File doesn''t have enough lines'
}