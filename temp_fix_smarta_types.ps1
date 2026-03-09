$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit ModelFamily a DeviceModel - mají být string, ne int?
$content = $content -replace "public int\? ModelFamily \{ get; set; \}", "public string? ModelFamily { get; set; }"
$content = $content -replace "public int\? DeviceModel \{ get; set; \}", "public string? DeviceModel { get; set; }"

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartaData.cs - ModelFamily and DeviceModel fixed to string"