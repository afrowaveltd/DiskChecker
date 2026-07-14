$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$all = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# Split into lines
$lines = $all -split "`n"

# Convert to ArrayList for easier manipulation
$arr = [System.Collections.ArrayList]@()
foreach ($l in $lines) {
    $arr.Add($l.TrimEnd("`r")) | Out-Null
}

Write-Output ("Total lines: " + $arr.Count)

# Find all method starts that are duplicated
# We need to find methods that have both old (ROW_NUMBER) and new (modulo) versions

# Method signatures to look for (these should appear exactly twice)
$signatures = @(
    "private static async Task<List<SpeedSample>> LoadSpeedSeriesDownsampledAsync",
    "public async Task<List<TemperatureSample>> GetTemperatureSampleSeriesDownsampledAsync",
    "private static async Task ConfigureSqliteReadOptimizationsAsync"
)

# For each signature, find all occurrences
foreach ($sig in $signatures) {
    $occurrences = @()
    for ($i = 0; $i -lt $arr.Count; $i++) {
        if ($arr[$i] -match [regex]::Escape($sig)) {
            $occurrences += $i
        }
    }
    Write-Output ("$sig : found at lines " + ($occurrences -join ", "))
}
