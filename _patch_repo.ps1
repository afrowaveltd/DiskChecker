$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# Add diagnostic logging to CreateCertificateAsync
# Add Stopwatch declaration after ArgumentNullException
$old1 = @'
    public async Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // Clear change tracker to prevent conflicts with owned entities
        // (SmartAttributeSummary) from previously loaded certificates.
        // Certificate generation creates a fresh DiskCertificate with new
        // SmartAttributeSummary owned entities (Id = 0). If the DbContext is
        // already tracking another certificate (e.g. from GetLatestCertificateAsync),
        // its SmartAttributes may have the same primary key values (auto-generated),
        // causing "another instance with the same key value" tracking errors.
        _context.ChangeTracker.Clear();
'@

$new1 = @'
    public async Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var diagSw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[DIAG] CreateCertificateAsync START");

        // Clear change tracker to prevent conflicts with owned entities
        // (SmartAttributeSummary) from previously loaded certificates.
        // Certificate generation creates a fresh DiskCertificate with new
        // SmartAttributeSummary owned entities (Id = 0). If the DbContext is
        // already tracking another certificate (e.g. from GetLatestCertificateAsync),
        // its SmartAttributes may have the same primary key values (auto-generated),
        // causing "another instance with the same key value" tracking errors.
        _context.ChangeTracker.Clear();
'@

if ($content.Contains($old1)) {
    $content = $content.Replace($old1, $new1)
    Write-Host "Replaced old1 successfully"
} else {
    Write-Host "old1 NOT found!"
}

# Add diagnostics before _context.DiskCertificates.Add
$old2 = @'
        _context.DiskCertificates.Add(certificate);
        await _context.SaveChangesAsync();

        var session = await _context.TestSessions.FindAsync(certificate.TestSessionId);
'@

$new2 = @'
        _context.DiskCertificates.Add(certificate);
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        await _context.SaveChangesAsync();
        sw1.Stop();
        System.Diagnostics.Debug.WriteLine($"[DIAG] CreateCertificate SaveChanges #1: {sw1.ElapsedMilliseconds}ms");

        var swFind = System.Diagnostics.Stopwatch.StartNew();
        var session = await _context.TestSessions.FindAsync(certificate.TestSessionId);
        swFind.Stop();
        System.Diagnostics.Debug.WriteLine($"[DIAG] CreateCertificate FindAsync: {swFind.ElapsedMilliseconds}ms, found={session != null}");
'@

if ($content.Contains($old2)) {
    $content = $content.Replace($old2, $new2)
    Write-Host "Replaced old2 successfully"
} else {
    Write-Host "old2 NOT found!"
}

# Add diagnostics to SaveChanges for session update
$old3 = @'
            session.CertificateId = certificate.Id;
            await _context.SaveChangesAsync();
        }

        return certificate;
'@

$new3 = @'
            session.CertificateId = certificate.Id;
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            await _context.SaveChangesAsync();
            sw2.Stop();
            System.Diagnostics.Debug.WriteLine($"[DIAG] CreateCertificate SaveChanges #2 (session link): {sw2.ElapsedMilliseconds}ms");
        }

        diagSw.Stop();
        System.Diagnostics.Debug.WriteLine($"[DIAG] CreateCertificateAsync TOTAL: {diagSw.ElapsedMilliseconds}ms");
        return certificate;
'@

if ($content.Contains($old3)) {
    $content = $content.Replace($old3, $new3)
    Write-Host "Replaced old3 successfully"
} else {
    Write-Host "old3 NOT found!"
}

[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
Write-Host "File written."
