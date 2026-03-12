$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs", [Text.Encoding]::UTF8)
# Find CurrentSelfTest parsing
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "CurrentSelfTest|self_test|nvme_self_test") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}