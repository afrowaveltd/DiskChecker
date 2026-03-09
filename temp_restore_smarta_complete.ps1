$utf8 = New-Object System.Text.UTF8Encoding $false

# Kompletně obnovit soubor z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$destPath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"

# Načíst původní obsah
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)

# Přidat IsCritical vlastnost do SmartaAttributeItem
$content = $content -replace "(public string WhenFailed \{ get; set; \} = string.Empty;)", "`$1`n    public bool IsCritical { get; set; }"

# Přidat extension metody na konec souboru (před poslední })
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

# Najít poslední } a vložit před něj
$lastBrace = $content.LastIndexOf("}")
if ($lastBrace -gt 0) {
    $newContent = $content.Substring(0, $lastBrace) + $extensionMethods + "`n}"
    [System.IO.File]::WriteAllText($destPath, $newContent, $utf8)
    Write-Output "SmartaData.cs fully restored and fixed"
    
    # Zkontrolovat
    $lines = $newContent -split "`n"
    Write-Output "Total lines: $($lines.Count)"
    Write-Output "=== Lines 25-35 ==="
    for ($i = 24; $i -lt 35 -and $i -lt $lines.Count; $i++) {
        Write-Output "$($i+1): $($lines[$i])"
    }
}