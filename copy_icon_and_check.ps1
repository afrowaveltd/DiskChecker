$bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\_Archived\DiskChecker.UI.WPF\Resources\icon-wpf.ico")
[System.IO.File]::WriteAllBytes("D:\DiskChecker\DiskChecker.UI.Avalonia\Assets\icon.ico", $bytes)
Write-Output "Icon copied: $($bytes.Length) bytes"

# Check DbContext constructor
Write-Output ""
Write-Output "=== DiskCheckerDbContext constructor ==="
$dbPath = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs"
$content = [System.IO.File]::ReadAllText($dbPath, [System.Text.Encoding]::UTF8)
# Find constructor
$lines = $content -split "`n"
for ($i = 0; $i -lt [Math]::Min(50, $lines.Count); $i++) {
    if ($lines[$i] -match "DbContext|public DiskCheckerDbContext|DbContextOptions") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}