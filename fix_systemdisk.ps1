$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs")
$encoding = [System.Text.Encoding]::UTF8
$content = $encoding.GetString($bytes)

# Create new LoadDataAsync method - simplified, single source of truth
$newMethod = @'
    private async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;
            LoadingState = "Načítám disky...";

            // Get list of locked disks from settings
            var lockedDisks = await _settingsService.GetLockedDisksAsync();

            // Get list of drives from detection service
            var drives = await _diskDetectionService.GetDrivesAsync();
            
            // Clear existing items
            DiskCards.Clear();
            
            // Load each drive and build disk cards
            foreach (var drive in drives)
            {
                try
                {
                    await LoadDriveAsync(drive, lockedDisks);
                }
                catch (Exception)
                {
                    // If we can't get SMART data, add the drive with basic info
                    AddBasicDriveCard(drive, lockedDisks);
                }
            }
            
            // SINGLE SOURCE OF TRUTH: Find system disk from the already-loaded disk cards
            // The disk card with IsSystemDisk = true is the system disk - use it for quick report
            var systemDiskCard = DiskCards.FirstOrDefault(c => c.IsSystemDisk);
            if (systemDiskCard != null)
            {
                UpdateSystemDiskReport(systemDiskCard.Drive, systemDiskCard.SmartData);
            }
            else
            {
                // No system disk found
                UpdateSystemDiskReport(null, null);
            }

            LoadingState = DiskCards.Count > 0 ? "Disky načteny" : "Žádné disky nalezeny";
            StatusMessage = $"Nalezeno {DiskCards.Count} disků";
        }
        catch (Exception ex)
        {
            LoadingState = $"Chyba: {ex.Message}";
            StatusMessage = "Nepodařilo se načíst disky";
        }
        finally
        {
            IsBusy = false;
        }
    }
'@

# Find start and end of LoadDataAsync method
$startPattern = 'private async Task LoadDataAsync()'
$endPattern = 'private async Task<SmartaData?> LoadDriveAsync'

$startIdx = $content.IndexOf($startPattern)
$endIdx = $content.IndexOf($endPattern)

if ($startIdx -ge 0 -and $endIdx -ge 0) {
    # Extract before and after
    $before = $content.Substring(0, $startIdx)
    $after = $content.Substring($endIdx)
    
    # Combine
    $newContent = $before + $newMethod + "`r`n`r`n    " + $after
    
    # Write back
    [System.IO.File]::WriteAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs", $encoding.GetBytes($newContent))
    Write-Output "File updated successfully"
} else {
    Write-Output "Could not find method boundaries: start=$startIdx, end=$endIdx"
}