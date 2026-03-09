$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.ProgressHandling.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Přidat using DiskChecker.UI.WPF.Models
if (-not ($content.Contains("using DiskChecker.UI.WPF.Models;"))) {
    $content = $content -replace "using DiskChecker.Core.Models;", "using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.Models;"
    [System.IO.File]::WriteAllText($filePath, $content, $utf8)
    Write-Output "Added using DiskChecker.UI.WPF.Models"
} else {
    Write-Output "Already has using"
}