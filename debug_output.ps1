# Script to run smartctl and show JSON output for self-test status
# This helps debug what smartctl returns on Windows

Write-Output "=== Checking smartctl status ==="

# Find smartctl
$smartctlPaths = @(
    "C:\Program Files\smartmontools\bin\smartctl.exe",
    "C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
    "C:\ProgramData\chocolatey\bin\smartctl.exe",
    "C:\tools\smartmontools\smartctl.exe"
)

$smartctl = $smartctlPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $smartctl) {
    Write-Output "smartctl not found in common paths"
    exit 1
}

Write-Output "Found smartctl at: $smartctl"

# Get drives
Write-Output "`n=== Listing drives ==="
& $smartctl --scan

# For each physical drive, get SMART data
for ($i = 0; $i -lt 5; $i++) {
    $devPath = "/dev/pd$i"
    Write-Output "`n=== SMART data for $devPath ==="
    $output = & $smartctl -j -a $devPath 2>&1
    
    # Try to parse and show self-test related info
    try {
        $json = $output | ConvertFrom-Json
        
        # Show ata_smart_data.self_test if present
        if ($json.ata_smart_data.self_test) {
            Write-Output "ATA self_test:"
            $json.ata_smart_data.self_test | ConvertTo-Json -Depth 5
        }
        
        # Show ata_smart_self_test_log if present  
        if ($json.ata_smart_self_test_log) {
            Write-Output "`nATA self_test_log:"
            $json.ata_smart_self_test_log | ConvertTo-Json -Depth 5
        }
        
        # Show nvme_self_test_log if present
        if ($json.nvme_self_test_log) {
            Write-Output "`nNVMe self_test_log:"
            $json.nvme_self_test_log | ConvertTo-Json -Depth 5
        }
        
        # Show device type
        if ($json.device) {
            Write-Output "`nDevice info:"
            $json.device | ConvertTo-Json -Depth 2
        }
    }
    catch {
        Write-Output "Failed to parse JSON: $_"
        Write-Output "Raw output preview: $($output.Substring(0, [Math]::Min(500, $output.Length)))"
    }
}