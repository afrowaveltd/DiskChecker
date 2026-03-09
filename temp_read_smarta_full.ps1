$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs", $utf8)
Write-Output $content