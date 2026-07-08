$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$content = [System.IO.File]::ReadAllText($file)

$old = 'Id = attr.Id,'
$new = 'AttributeId = attr.Id,'

if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content)
    Write-Output "SUCCESS: Id = attr.Id -> AttributeId = attr.Id"
} else {
    Write-Output "FAILED: not found"
}
