$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Přidáme using DiskChecker.UI.WPF.Models
$oldUsing = "using System.IO;

namespace DiskChecker.UI.WPF.ViewModels;"

$newUsing = "using System.IO;
using DiskChecker.UI.WPF.Models;

namespace DiskChecker.UI.WPF.ViewModels;"

$newContent = $content.Replace($oldUsing, $newUsing)
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SurfaceTestViewModel updated"