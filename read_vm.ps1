$content = [System.IO.File]::ReadAllText('DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs')
$lines = $content -split "`n"
for ($i = 1134; $i -lt 1290; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}