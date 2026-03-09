$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Nahradíme using sekci
$oldUsing = "using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static System.Console;"

$newUsing = "using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static System.Console;"

$newContent = $content.Replace($oldUsing, $newUsing)
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "File updated successfully"