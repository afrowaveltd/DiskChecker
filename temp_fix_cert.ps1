$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\Services\SurfaceTestCertificateDocumentBuilder.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Řádek 189: powerOnHours?.ToString() ?? "N/A" - správný formát
$content = $content -replace 'data\.PowerOnHours\.ToString\(\)', 'data.PowerOnHours?.ToString() ?? "N/A"'

# Řádek 199: text ?? string.Empty - přidat null handling
$content = $content -replace 'AddCell\(grid, 2, 1, data\.PowerOnHours\.ToString\(\), false\)', 'AddCell(grid, 2, 1, data.PowerOnHours?.ToString() ?? "N/A", false)'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SurfaceTestCertificateDocumentBuilder.cs updated"