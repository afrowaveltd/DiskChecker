$utf8 = New-Object System.Text.UTF8Encoding $false
$speedSampleCorePath = "D:\DiskChecker\DiskChecker.Core\Models\SurfaceTestModels.cs"
$content = [System.IO.File]::ReadAllText($speedSampleCorePath, $utf8)
Write-Output $content