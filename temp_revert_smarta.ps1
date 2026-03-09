$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Vrátit zpět na int?
$content = $content -replace "public string\? ModelFamily \{ get; set; \}", "public int? ModelFamily { get; set; }"
$content = $content -replace "public string\? DeviceModel \{ get; set; \}", "public int? DeviceModel { get; set; }"

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartaData.cs - reverted ModelFamily and DeviceModel to int?"