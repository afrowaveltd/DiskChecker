$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs", [Text.Encoding]::UTF8)
# Show ParseNvmeSelfTestLog method (lines 653-800)
for ($i = 652; $i -lt 800; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}