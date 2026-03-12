$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show the polling loop progress detection
for ($i = 1138; $i -lt 1170; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}