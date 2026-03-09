$content = [System.IO.File]::ReadAllText("D:\DiskChecker\errors_only.txt", [System.Text.Encoding]::UTF8)
Write-Output $content