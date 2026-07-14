$root = 'D:\DiskChecker'
$results = @()

# Find TestSession class
Get-ChildItem -Path $root -Filter '*.cs' -Recurse | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -Encoding UTF8
    if ($content -match 'class\s+TestSession') {
        $results += "TestSession class in: $($_.FullName)"
    }
    if ($content -match 'OwnsMany') {
        $results += "OwnsMany in: $($_.FullName)"
    }
    if ($content -match 'GetCertificateAsync') {
        $results += "GetCertificateAsync in: $($_.FullName)"
    }
}

$results | Out-File -FilePath 'D:\DiskChecker\_search_results.txt' -Encoding UTF8
Write-Host "Done, found $($results.Count) matches"
