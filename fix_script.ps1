# Fix CertificateGenerator.cs type conversion errors

# Read the file content
$filePath = "D:\DiskChecker\DiskChecker.Core\Services\CertificateGenerator.cs"
$content = Get-Content -Path $filePath -Raw

Write-Host "Original content length: $($content.Length) characters"

# Split into lines 
$lines = $content -split "`r?`n"
Write-Host "Number of lines: $($lines.Count)"

# Show the problematic lines
if ($lines.Count -gt 85) {
    Write-Host "Line 84 (index 83): $($lines[83])"
    Write-Host "Line 85 (index 84): $($lines[84])"
    
    # Fix these specific lines
    $backup84 = $lines[83]
    $backup85 = $lines[84]
    
    # Fix ModelFamily pattern
    if ($lines[83] -match "ModelFamily.*\?\?") {
        Write-Host "Found ModelFamily pattern in line 84"
        $lines[83] = $lines[83] -replace "smartaData\.ModelFamily\s*\?\?", "smartaData.ModelFamily?.ToString()"
        Write-Host "Fixed line 84: $($lines[83])"
    }
    
    # Fix DeviceModel pattern
    if ($lines[84] -match "DeviceModel.*\?\?") {
        Write-Host "Found DeviceModel pattern in line 85"
        $lines[84] = $lines[84] -replace "smartaData\.DeviceModel\s*\?\?", "smartaData.DeviceModel?.ToString()"
        Write-Host "Fixed line 85: $($lines[84])"
    }
    
    # Write back only if changes were made
    if (($lines[83] -ne $backup84) -or ($lines[84] -ne $backup85)) {
        $fixedContent = $lines -join "`r`n"
        Set-Content -Path $filePath -Value $fixedContent -Encoding UTF8
        Write-Host "CertificateGenerator.cs has been updated successfully"
    } else {
        Write-Host "No changes made to the file"
    }
} else {
    Write-Host "File doesn't have enough lines"
}