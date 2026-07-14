$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# Find the method start and add ChangeTracker.Clear()
$old = 'if (certificate.GeneratedAt == default)
        {
            certificate.GeneratedAt = DateTime.UtcNow;
        }
        
        _context.DiskCertificates.Add(certificate);'

$new = 'if (certificate.GeneratedAt == default)
        {
            certificate.GeneratedAt = DateTime.UtcNow;
        }

        // Prevent EF Core from tracking accumulated entities from previous queries
        // (critical: avoids 2TB virtual memory reservation from tracked sample collections)
        _context.ChangeTracker.Clear();
        
        _context.DiskCertificates.Add(certificate);'

if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "FIXED: Added ChangeTracker.Clear() before Add(certificate)"
} else {
    Write-Host "ERROR: Pattern not found"
}
