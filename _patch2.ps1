$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs'
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

$old = 'var result = await _certificateExportService.ExportCertificateAsync('
$new = 'System.Diagnostics.Debug.WriteLine("[DIAG] GenerateCertificateAsync: About to call ExportCertificateAsync for session={0}", targetSession.Id);
            var result = await _certificateExportService.ExportCertificateAsync('

$content = $content.Replace($old, $new)

if ($content -notmatch 'using System.Diagnostics;') {
    $content = $content.Replace('using OxyPlot.Series;', 'using OxyPlot.Series;' + [Environment]::NewLine + 'using System.Diagnostics;')
}

[System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Done"
