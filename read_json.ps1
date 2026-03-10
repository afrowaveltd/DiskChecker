$path = "C:\Users\lo505926\AppData\Local\Temp\smart_test_output.json"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
Write-Output $content