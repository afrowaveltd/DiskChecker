# Test smartctl JSON output for debugging
$smartctlPath = @(
    "smartctl",
    "C:\Program Files\smartmontools\bin\smartctl.exe",
    "C:\Program Files (x86)\smartmontools\bin\smartctl.exe"
) | Where-Object { 
    if ($_ -eq "smartctl") { 
        $cmd = Get-Command smartctl -ErrorAction SilentlyContinue
        $cmd -ne $null 
    } else { 
        Test-Path $_ 
    }
} | Select-Object -First 1

if (-not $smartctlPath) {
    Write-Host "smartctl not found!"
    exit 1
}

Write-Host "Using smartctl: $smartctlPath"

# Test first physical drive - try both formats
$devPath = "\\.\PhysicalDrive0"
$args = "-j -a `"$devPath`""

Write-Host "`nRunning: $smartctlPath $args"
$json = & $smartctlPath $args.Split(' ') 2>&1 | Out-String

# Save to temp file
$tempPath = Join-Path $env:TEMP "smart_test_output.json"
$json | Out-File -FilePath $tempPath -Encoding UTF8

Write-Host "`nJSON saved to: $tempPath"
Write-Host "JSON length: $($json.Length) chars"

# Check for error in JSON
if ($json -match "UNRECOGNIZED" -or $json -match "error") {
    Write-Host "`n!!! Error in smartctl output - trying Cygwin format !!!"
    $devPath = "/dev/pd0"
    $args = "-j -a $devPath"
    Write-Host "Running: $smartctlPath $args"
    $json = & $smartctlPath $args.Split(' ') 2>&1 | Out-String
    Write-Host "JSON length: $($json.Length) chars"
}

# Parse JSON
try {
    $parsed = $json | ConvertFrom-Json -ErrorAction Stop
    
    Write-Host "`n=== Device Info ==="
    Write-Host "Model: $($parsed.model_name)"
    Write-Host "Serial: $($parsed.serial_number)"
    Write-Host "Firmware: $($parsed.firmware_version)"
    Write-Host "Device type: $($parsed.device.type)"
    
    Write-Host "`n=== SMART Status ==="
    Write-Host "Passed: $($parsed.smart_status.passed)"
    
    # Check for ATA SMART attributes
    if ($parsed.ata_smart_attributes) {
        Write-Host "`n=== ATA SMART Attributes ==="
        Write-Host "ata_smart_attributes FOUND"
        if ($parsed.ata_smart_attributes.table) {
            $table = $parsed.ata_smart_attributes.table
            Write-Host "Table has $($table.Count) attributes"
            Write-Host "`nFirst 5 attributes:"
            $table | Select-Object -First 5 | ForEach-Object {
                Write-Host "  ID $($_.id): $($_.name) = $($_.value) (worst: $($_.worst), thresh: $($_.thresh))"
            }
        } else {
            Write-Host "NO 'table' property in ata_smart_attributes!"
        }
    } else {
        Write-Host "`nNO ata_smart_attributes found!"
    }
    
    # Check for NVMe
    if ($parsed.nvme_smart_health_information_log) {
        Write-Host "`n=== NVMe SMART Health Log ==="
        Write-Host "temperature: $($parsed.nvme_smart_health_information_log.temperature)"
        Write-Host "power_on_hours: $($parsed.nvme_smart_health_information_log.power_on_hours)"
        Write-Host "power_cycles: $($parsed.nvme_smart_health_information_log.power_cycles)"
    }
    
    # Check for SCSI
    if ($parsed.PSObject.Properties.Match("scsi_grown_defect_list").Count -gt 0) {
        Write-Host "`n=== SCSI Defects ==="
        Write-Host "scsi_grown_defect_list: $($parsed.scsi_grown_defect_list)"
    }
    
} catch {
    Write-Host "`nERROR parsing JSON: $_"
    Write-Host "Raw JSON preview (first 500 chars):"
    Write-Host $json.Substring(0, [Math]::Min(500, $json.Length))
}

Write-Host "`n=== Done ==="