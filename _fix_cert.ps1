$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

$oldPattern = '.Include(c => c.TestSession)' + [Environment]::NewLine
$newPattern = '.AsNoTracking()' + [Environment]::NewLine

if ($content -match [regex]::Escape('.Include(c => c.TestSession)')) {
    $content = $content.Replace('.Include(c => c.TestSession)' + [Environment]::NewLine + '        ', '.AsNoTracking()' + [Environment]::NewLine + '        ')
    [System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "FIXED: Removed .Include(c => c.TestSession), added .AsNoTracking()"
} else {
    Write-Host "ERROR: Pattern not found!"
}
