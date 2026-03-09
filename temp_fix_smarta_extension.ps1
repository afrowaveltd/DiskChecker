$utf8 = New-Object System.Text.UTF8Encoding $false

# Načíst původní soubor z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)

# Přidat IsCritical vlastnost do SmartaAttributeItem
$content = $content -replace "(public string WhenFailed \{ get; set; \} = string.Empty;)", "`$1`n    public bool IsCritical { get; set; }"

# Najít poslední } a přidat extension metody PŘED něj (uvnitř namespace)
$extensionMethods = @"

// Extension methods for SmartaSelfTestStatus
public static class SmartaSelfTestStatusExtensions
{
    public static bool IsRunning(this SmartaSelfTestStatus status)
    {
        return status == SmartaSelfTestStatus.InProgress;
    }
    
    public static string StatusText(this SmartaSelfTestStatus status)
    {
        return status switch
        {
            SmartaSelfTestStatus.Unknown => "Unknown status",
            SmartaSelfTestStatus.CompletedWithoutError => "Test completed successfully",
            SmartaSelfTestStatus.AbortedByUser => "Test was aborted by user",
            SmartaSelfTestStatus.AbortedByHost => "Test was interrupted by host",
            SmartaSelfTestStatus.FatalError => "Fatal error during test",
            SmartaSelfTestStatus.ErrorUnknown => "Unknown error during test",
            SmartaSelfTestStatus.ErrorElectrical => "Electrical error detected",
            SmartaSelfTestStatus.ErrorServo => "Servo error detected",
            SmartaSelfTestStatus.ErrorRead => "Read error detected",
            SmartaSelfTestStatus.ErrorHandling => "Handling error detected",
            SmartaSelfTestStatus.InProgress => "Test is currently running",
            _ => "Unknown status"
        };
    }
    
    public static int GetRemainingPercent(this SmartaSelfTestStatus status)
    {
        return status == SmartaSelfTestStatus.InProgress ? 50 : 100;
    }
}
"@

# File-scoped namespace - najít poslední } souboru
$lastBrace = $content.LastIndexOf("}")
if ($lastBrace -gt 0) {
    # Najít předchozí } (konec poslední třídy před extension)
    $prevBrace = $content.LastIndexOf("}", $lastBrace - 1)
    if ($prevBrace -gt 0) {
        # Vložit extension metody za předchozí } a před poslední }
        $newContent = $content.Substring(0, $prevBrace + 1) + $extensionMethods + "`n}"
        [System.IO.File]::WriteAllText("D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs", $newContent, $utf8)
        Write-Output "SmartaData.cs fixed with extension methods outside classes"
        
        # Zkontrolovat
        $lines = $newContent -split "`n"
        Write-Output "Total lines: $($lines.Count)"
        Write-Output "=== Last 15 lines ==="
        for ($i = [Math]::Max(0, $lines.Count - 15); $i -lt $lines.Count; $i++) {
            Write-Output "$($i+1): $($lines[$i])"
        }
    }
}