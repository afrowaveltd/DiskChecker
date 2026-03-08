Get-Content -Path "DiskChecker.UI.Avalonia\ViewModels\HistoryViewModel.cs" | Select-Object -First 120 | Select-String -Pattern "GetTestByIdAsync|DeleteHistoricalTestAsync" -Context 1,1
