$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs'
$content = Get-Content -Path $file -Raw -Encoding UTF8

$old = 'StatusMessage = L.Get("DiskCardDetail.Status.GeneratingCert");

            // Use selected session if available, otherwise fall back to latest
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);'

$new = 'StatusMessage = L.Get("DiskCardDetail.Status.GeneratingCert");

            System.Diagnostics.Debug.WriteLine("[DIAG] GenerateCertificateAsync: Starting, cardId={0}", CurrentCard.Id);
            System.Diagnostics.Debug.WriteLine("[DIAG] GenerateCertificateAsync: About to call GetTestSessionsAsync...");

            // Use selected session if available, otherwise fall back to latest
            var sessions = await _diskCardRepository.GetTestSessionsAsync(CurrentCard.Id);
            System.Diagnostics.Debug.WriteLine("[DIAG] GenerateCertificateAsync: GetTestSessionsAsync returned {0} sessions", sessions?.Count ?? 0);'

$content = $content.Replace($old, $new)
Set-Content -Path $file -Value $content -Encoding UTF8 -NoNewline
Write-Host "Done"
