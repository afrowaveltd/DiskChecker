$utf8 = New-Object System.Text.UTF8Encoding $false

# Načíst původní soubor z Previous
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($prevPath, $utf8)
$lines = $content -split "`n"

# Vytvořit nový obsah bez CoreDriveInfo a SmartaSelfTestReport
$newLines = @()
$skipBlock = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Přeskočit CoreDriveInfo třídu
    if ($line -match "^public class CoreDriveInfo" -or $line -match "^public record CoreDriveInfo") {
        $skipBlock = $true
        continue
    }
    
    # Přeskočit SmartaSelfTestReport třídu
    if ($line -match "^public class SmartaSelfTestReport" -or $line -match "^public record SmartaSelfTestReport") {
        $skipBlock = $true
        continue
    }
    
    # Konec bloku
    if ($skipBlock) {
        if ($line -match "^}") {
            $skipBlock = $false
        }
        continue
    }
    
    # Přidat IsCritical vlastnost do SmartaAttributeItem
    if ($line -match "public string WhenFailed.*string.Empty") {
        $newLines += $line
        $newLines += "    public bool IsCritical { get; set; }"
        continue
    }
    
    $newLines += $line
}

# Extension metody musí být mimo všechny třídy
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

# Najít poslední } a přidat extension metody před něj
$newContent = $newLines -join "`n"
$newContent = $newContent.TrimEnd()
if ($newContent.EndsWith("}")) {
    $newContent = $newContent.Substring(0, $newContent.Length - 1)
}
$newContent = $newContent + $extensionMethods + "`n}"

# Uložit
$destPath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
[System.IO.File]::WriteAllText($destPath, $newContent, $utf8)
Write-Output "SmartaData.cs completely fixed"

# Zkontrolovat výsledek
$finalLines = $newContent -split "`n"
Write-Output "Total lines: $($finalLines.Count)"

# Hledat duplicity
Write-Output ""
Write-Output "=== Checking for duplicates ==="
$dups = $finalLines | Where-Object { $_ -match "public class CoreDriveInfo|public class SmartaSelfTestReport" }
Write-Output "Found duplicates: $($dups.Count)"
foreach ($dup in $dups) {
    Write-Output $dup
}