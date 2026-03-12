$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
# Show RunSelfTestAsync method and StartPolling call
Write-Output "--- RunSelfTestAsync calls ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "RunSelfTestAsync\(") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}

# Show where StartPollingSelfTestProgressCommand is executed
Write-Output "`n--- StartPollingSelfTestProgressCommand calls ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "StartPollingSelfTestProgressCommand") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}