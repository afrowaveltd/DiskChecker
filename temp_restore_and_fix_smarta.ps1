$utf8 = New-Object System.Text.UTF8Encoding $false

# Obnovit původní SmartaData.cs z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$destPath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)
[System.IO.File]::WriteAllText($destPath, $content, $utf8)
Write-Output "SmartaData.cs restored from Previous"

# Zkontrolovat počet řádků
$lines = $content -split "`n"
Write-Output "Lines: $($lines.Count)"

# Přidat chybějící extension metody pro SmartaSelfTestStatus
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

# Přidat extension metody na konec souboru (před poslední })
$fileContent = [System.IO.File]::ReadAllText($destPath, $utf8)
$fileContent = $fileContent.TrimEnd()
$fileContent = $fileContent.TrimEnd('}')
$fileContent = $extensionMethods + "`n}"
[System.IO.File]::WriteAllText($destPath, $fileContent, $utf8)
Write-Output "Extension methods added"