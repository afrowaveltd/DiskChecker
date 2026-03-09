$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Přidáme using DiskChecker.UI.WPF.Models
$oldUsing = "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"

$newUsing = "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.Models;"

$newContent = $content.Replace($oldUsing, $newUsing)
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "ReportViewModel using updated"