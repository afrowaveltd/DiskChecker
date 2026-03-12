$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs", [Text.Encoding]::UTF8)
# Show NVMe parsing code (lines 492-610)
for ($i = 491; $i -lt 610; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}