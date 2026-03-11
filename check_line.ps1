$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs')
$lines = $content -split "`n"
Write-Output "Lines 304-310:"
for ($i = 303; $i -lt 310; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}