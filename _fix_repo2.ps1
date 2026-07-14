# Read the file as lines
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.Collections.ArrayList]@([System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8))

# Find method boundaries
$tempDownsampleStart = -1
$tempDownsampleEnd = -1
$speedDownsampleStart = -1
$speedDownsampleEnd = -1
$configureStart = -1
$configureEnd = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public async Task.*GetTemperatureSampleSeriesDownsampledAsync") {
        $tempDownsampleStart = $i - 1  # include the XML comment
        Write-Output "TempDownsample start: $i"
    }
    if ($lines[$i] -match "private static async Task.*LoadSpeedSeriesDownsampledAsync") {
        $speedDownsampleStart = $i - 1  # include the XML comment
        Write-Output "SpeedDownsample start: $i"
    }
    if ($lines[$i] -match "private static async Task ConfigureSqliteReadOptimizationsAsync") {
        $configureStart = $i
        Write-Output "Configure start: $i"
    }
}

# Find ends by tracking braces
function Find-MethodEnd($startLine) {
    $depth = 0
    $started = $false
    for ($i = $startLine; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '\{') { $depth += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count; $started = $true }
        if ($line -match '\}') { $depth -= ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count }
        if ($started -and $depth -eq 0) { return $i }
    }
    return $lines.Count - 1
}

$tempDownsampleEnd = Find-MethodEnd ($tempDownsampleStart + 1)
$speedDownsampleEnd = Find-MethodEnd ($speedDownsampleStart + 1)
$configureEnd = Find-MethodEnd $configureStart

Write-Output "TempDownsample: $tempDownsampleStart - $tempDownsampleEnd"
Write-Output "SpeedDownsample: $speedDownsampleStart - $speedDownsampleEnd"
Write-Output "Configure: $configureStart - $configureEnd"
