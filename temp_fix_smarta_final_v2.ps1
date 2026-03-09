$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

$newLines = @()
$skipBlock = $false
$blockStart = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Přeskočit CoreDriveInfo třídu (je v samostatném souboru)
    if ($line -match "^public class CoreDriveInfo") {
        $skipBlock = $true
        $blockStart = $i
        continue
    }
    
    # Přeskočit SmartaSelfTestReport třídu (je v samostatném souboru)
    if ($line -match "^public class SmartaSelfTestReport") {
        $skipBlock = $true
        $blockStart = $i
        continue
    }
    
    # Přeskočit SmartaSelfTestStatusExtensions třídu (přesuneme ji mimo namespace)
    if ($line -match "^public static class SmartaSelfTestStatusExtensions") {
        $skipBlock = $true
        $blockStart = $i
        continue
    }
    
    # Konec bloku
    if ($skipBlock -and $line -match "^}") {
        $skipBlock = $false
        continue
    }
    
    if ($skipBlock) {
        continue
    }
    
    $newLines += $line
}

# Najít poslední } (konec namespace) a vložit extension metody před něj
$newContent = $newLines -join "`n"

# Extension metody na konci souboru, ale uvnitř namespace
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

# Najít poslední } a nahradit
$newContent = $newContent.TrimEnd()
if ($newContent.EndsWith("}")) {
    $newContent = $newContent.Substring(0, $newContent.Length - 1)
}
$newContent = $newContent + $extensionMethods + "`n}"

[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SmartaData.cs fixed - removed duplicates and fixed extension methods"

# Zkontrolovat výsledek
$finalLines = $newContent -split "`n"
Write-Output "Total lines: $($finalLines.Count)"
Write-Output "=== Last 5 lines ==="
for ($i = [Math]::Max(0, $finalLines.Count - 5); $i -lt $finalLines.Count; $i++) {
    Write-Output "$($i+1): $($finalLines[$i])"
}