$content = [IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
$content = $content -replace 'private int _currentPhase = 0; // 0 = Write, 1 = Read', 'private int _currentPhase; // 0 = Write, 1 = Read (default is 0)'
[IO.File]::WriteAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", $content, [Text.Encoding]::UTF8)
Write-Output "Fixed _currentPhase initialization"