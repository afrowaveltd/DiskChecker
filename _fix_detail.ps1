$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs'
$content = [System.IO.File]::ReadAllText($file)

# Fix: Replace latestSession with targetSession (SelectedSession ?? sessions.FirstOrDefault())
$old = '            // Get latest test session ID
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            var latestSession = sessions.FirstOrDefault();

            if (latestSession == null)'

$new = '            // Use selected session if available, otherwise fall back to latest
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            var targetSession = SelectedSession ?? sessions.FirstOrDefault();

            if (targetSession == null)'

if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content)
    Write-Host "SUCCESS: Block replaced"
} else {
    Write-Host "FAILED: Old block not found"
    # Show what's there
    $lines = $content -split "`n"
    for ($i = 365; $i -le 375; $i++) {
        Write-Host "L$($i+1): $($lines[$i])"
    }
}
