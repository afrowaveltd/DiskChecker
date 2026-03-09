$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\AnalysisViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Přidáme using DiskChecker.Application.Models
$oldUsing = "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"

$newUsing = "using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;"

$newContent = $content.Replace($oldUsing, $newUsing)
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "AnalysisViewModel updated"