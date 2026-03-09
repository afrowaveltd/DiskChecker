$lines = Get-Content 'D:\DiskChecker\DiskChecker.UI.WPF\Services\SurfaceTestCertificateDocumentBuilder.cs'
Write-Output "Total lines: $($lines.Count)"
Write-Output "Line 189: $($lines[188])"
Write-Output "Line 199: $($lines[198])"
Write-Output "Line 186: $($lines[185])"
Write-Output "Line 187: $($lines[186])"
Write-Output "Line 188: $($lines[187])"