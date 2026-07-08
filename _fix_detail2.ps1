$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs'
$content = [System.IO.File]::ReadAllText($file)

# Use LF line endings in the replacement strings
$old = "            // Get latest test session ID`n            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);`n            var latestSession = sessions.FirstOrDefault();`n`n            if (latestSession == null)"

$new = "            // Use selected session if available, otherwise fall back to latest`n            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);`n            var targetSession = SelectedSession ?? sessions.FirstOrDefault();`n`n            if (targetSession == null)"

if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content)
    Write-Host "SUCCESS: Block replaced"
} else {
    Write-Host "FAILED: Old block not found"
}
