$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít a odstranit duplicity + přesunout extension metody
$newLines = @()
$skipUntil = -1
$inExtensionClass = $false
$foundExtensionStart = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $lineNum = $i + 1
    
    # Přeskočit CoreDriveInfo definici (je v samostatném souboru)
    if ($line -match "^public (class|record) CoreDriveInfo") {
        $skipUntil = $i
        while ($skipUntil -lt $lines.Count -and $lines[$skipUntil] -notmatch "^}") {
            $skipUntil++
        }
        continue
    }
    
    # Přeskočit SmartaSelfTestReport definici (je v samostatném souboru)
    if ($line -match "^public (class|record) SmartaSelfTestReport") {
        $skipUntil = $i
        while ($skipUntil -lt $lines.Count -and $lines[$skipUntil] -notmatch "^}") {
            $skipUntil++
        }
        continue
    }
    
    # Přeskočit uzavírací } po duplicitech
    if ($skipUntil -ge 0 -and $i -eq $skipUntil) {
        continue
    }
    
    # Extension metody - najít začátek
    if ($line -match "public static class SmartaSelfTestStatusExtensions") {
        $inExtensionClass = $true
        $foundExtensionStart = $true
        # Přesunout na konec souboru (mimo namespace)
        continue
    }
    
    if ($inExtensionClass) {
        if ($line -match "^}") {
            $inExtensionClass = $false
        }
        continue
    }
    
    $newLines += $line
}

# Extension metody přidat na konec (mimo namespace)
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
$newContent = $newLines -join "`n"
$newContent = $newContent.TrimEnd()
if ($newContent.EndsWith("}")) {
    $newContent = $newContent.Substring(0, $newContent.Length - 1)
}

$newContent = $newContent + $extensionMethods + "`n}"

[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SmartaData.cs fixed - removed duplicates and moved extension methods"

# Zkontroluj výsledek
$finalContent = [System.IO.File]::ReadAllText($filePath, $utf8)
$finalLines = $finalContent -split "`n"
Write-Output "Total lines: $($finalLines.Count)"