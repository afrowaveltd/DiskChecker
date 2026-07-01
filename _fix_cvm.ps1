$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs'
$content = Get-Content $file -Raw

# Fix the double-backslash TimeSpan format in UpdateDetailedSummaries
# The line is: $"Chyby: {totalErrors} | Doba: {session.Duration:hh\\\\:mm\\\\:ss}";
# Should be: $"Chyby: {totalErrors} | Doba: {session.Duration:hh\\:mm\\:ss}";
$content = $content -replace 'session\.Duration:hh\\\\:mm\\\\:ss', 'session.Duration:hh\\:mm\\:ss'

Set-Content $file -Value $content -NoNewline
Write-Host "Done fixing CertificateViewModel.cs"
