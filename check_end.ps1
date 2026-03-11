$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

# Najít konec ParseNvmeSelfTestLog metody
for ($i = 698; $i -lt 750; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}