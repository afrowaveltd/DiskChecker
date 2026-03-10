# Check icon file
$iconPath = "D:\DiskChecker\_Archived\DiskChecker.UI.WPF\Resources\icon-wpf.ico"
if (Test-Path $iconPath) {
    $file = Get-Item $iconPath
    Write-Output "Icon found: $($file.FullName), Size: $($file.Length) bytes"
} else {
    Write-Output "Icon NOT found at $iconPath"
}

# Check App.axaml.cs for DbContext configuration
Write-Output ""
Write-Output "=== App.axaml.cs ==="
$appPath = "D:\DiskChecker\DiskChecker.UI.Avalonia\App.axaml.cs"
$bytes = [System.IO.File]::ReadAllBytes($appPath)
$content = [System.Text.Encoding]::UTF8.GetString($bytes)
Write-Output $content