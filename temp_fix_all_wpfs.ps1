$utf8 = New-Object System.Text.UTF8Encoding $false

# Fix AnalysisViewModel.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\AnalysisViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "using CommunityToolkit.Mvvm.ComponentModel;\r?\nusing CommunityToolkit.Mvvm.Input;\r?\nusing DiskChecker.Application.Services;\r?\nusing DiskChecker.Core.Models;", "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "AnalysisViewModel.cs fixed"

# Fix SettingsViewModel.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "using CommunityToolkit.Mvvm.ComponentModel;\r?\nusing CommunityToolkit.Mvvm.Input;\r?\nusing DiskChecker.Application.Services;\r?\nusing DiskChecker.Core.Interfaces;", "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SettingsViewModel.cs fixed"

# Fix HistoryViewModel.cs  
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\HistoryViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "using CommunityToolkit.Mvvm.ComponentModel;\r?\nusing CommunityToolkit.Mvvm.Input;\r?\nusing DiskChecker.Application.Services;\r?\nusing DiskChecker.Core.Models;", "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "HistoryViewModel.cs fixed"

# Fix ReportViewModel.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "using CommunityToolkit.Mvvm.ComponentModel;\r?\nusing CommunityToolkit.Mvvm.Input;\r?\nusing DiskChecker.Application.Services;\r?\nusing DiskChecker.Core.Models;", "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "ReportViewModel.cs fixed"

# Fix SurfaceTestViewModel.cs - add using DiskChecker.UI.WPF.Models
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "using System.IO;\r?\n\r?\nnamespace DiskChecker.UI.WPF.ViewModels;", "using System.IO;
using DiskChecker.UI.WPF.Models;

namespace DiskChecker.UI.WPF.ViewModels;"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SurfaceTestViewModel.cs fixed"