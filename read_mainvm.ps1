$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\MainWindowViewModel.cs"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content