# Read the CertificateGenerator.cs file
$content = Get-Content -Path "D:\DiskChecker\DiskChecker.Core\Services\CertificateGenerator.cs" -Raw

# Split into lines
$lines = $content -split "`r?`n"

Write-Host "CertificateGenerator.cs has" $lines.Count "lines"

# Check the problematic lines 84-85 (index 83-84)
if ($lines.Count -gt 84) {
    Write-Host "Original Line 84 (index 83):" $lines[83]
    Write-Host "Original Line 85 (index 84):" $lines[84]
    
    $original84 = $lines[83]
    $original85 = $lines[84]
    
    # Fix ModelFamily int??string pattern - line 84
    if ($lines[83] -match "ModelFamily.*\?\?") {
        Write-Host "Fixing ModelFamily pattern in line 84"
        $lines[83] = $lines[83] -replace "(smartaData\.ModelFamily\s*\?\?)", "smartaData.ModelFamily?.ToString()"
        Write-Host "Fixed Line 84:" $lines[83]
    }
    
    # Fix DeviceModel int??string pattern - line 85  
    if ($lines[84] -match "DeviceModel.*\?\?") {
        Write-Host "Fixing DeviceModel pattern in line 85"
        $lines[84] = $lines[84] -replace "(smartaData\.DeviceModel\s*\?\?)", "smartaData.DeviceModel?.ToString()"  
        Write-Host "Fixed Line 85:" $lines[84]
    }
    
    # Write back the fixed content
    if ($lines[83] -ne $original84 -or $lines[84] -ne $original85) {
        $fixedContent = $lines -join "`r`n"
        Set-Content -Path "D:\DiskChecker\DiskChecker.Core\Services\CertificateGenerator.cs" -Value $fixedContent -Encoding UTF8
        Write-Host "CertificateGenerator.cs has been updated successfully"
    } else {
        Write-Host "No changes needed"
    }
} else {
    Write-Host "File doesn't have enough lines"
}

Write-Host "Done"