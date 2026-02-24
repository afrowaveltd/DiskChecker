// Quick test - run this to diagnose Windows disk detection
// Paste this into PowerShell (Admin mode) to test:
/*
Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size | ConvertTo-Json -AsArray

# Or simpler:
Get-CimInstance Win32_DiskDrive | Format-Table DeviceID, Model, Size -AutoSize

# To test physical disk info:
Get-PhysicalDisk | Format-Table DeviceId, Model, Size -AutoSize

# To test StorageReliabilityCounter:
Get-PhysicalDisk | Get-StorageReliabilityCounter
*/

// The issue might be:
// 1. Program not running as Administrator
// 2. PowerShell execution policy
// 3. WMI not responding
// 4. No physical disks in system
// 5. Virtual environment without proper disk access

namespace DiskChecker.Tests
{
    // Diagnostic notes for Windows SMART detection
    public class WindowsSmartDiagnosticNotes
    {
        public const string Note = @"
WINDOWS SMART DETECTION TROUBLESHOOTING
========================================

If no disks are detected, check these issues:

1. ADMINISTRATOR RIGHTS
   - Program MUST run as Administrator
   - Open PowerShell as Admin and run:
     Get-CimInstance Win32_DiskDrive

2. WMI SERVICE
   - Check if Windows Management Instrumentation (WMI) is running
   - Run: Get-Service WinRM
   - Or restart: Restart-Service WinRM

3. POWERSHELL EXECUTION POLICY
   - Run in Admin PowerShell:
     Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

4. VIRTUAL MACHINES
   - VM hypervisors may not expose physical disk info
   - Some VMs need special drivers for SMART access

5. EXTERNAL/USB DRIVES
   - May not appear in Win32_DiskDrive
   - Check with: Get-Disk | Where-Object BusType -eq USB

6. SMARTMONTOOLS (OPTIONAL)
   - For detailed SMART data, install via:
     winget install smartmontools
   - Or download from: https://www.smartmontools.org/

TESTING COMMANDS IN POWERSHELL (ADMIN):
========================================

# List all drives
Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size

# List physical disks
Get-PhysicalDisk | Format-Table DeviceId, Model, Size

# Get storage reliability data
Get-PhysicalDisk | Get-StorageReliabilityCounter

# Test smartctl (if installed)
smartctl --scan
smartctl -a \\.\PhysicalDrive0
";
    }
}
