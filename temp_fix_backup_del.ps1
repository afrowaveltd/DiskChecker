$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Zkopírovat BackupService.cs z Previous a přidat potřebné metody
$prevPath = "D:\DiskChecker\Previous\DiskChecker.UI.WPF\Services\BackupService.cs"
$destPath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"

# Použijeme existující soubor ale opravíme strukturu
$content = [System.IO.File]::ReadAllText($destPath, $utf8)

# Najít poslední } a přidat DeleteBackupAsync před ni
if ($content -notmatch "DeleteBackupAsync") {
    # Najít poslední } třídy BackupService (ne poslední } souboru)
    $lastClassEnd = $content.LastIndexOf("}`r`n")
    if ($lastClassEnd -lt 0) {
        $lastClassEnd = $content.LastIndexOf("}`n")
    }
    
    # Vložit DeleteBackupAsync před poslední }
    $deleteBackupMethod = @"

    public async Task DeleteBackupAsync(string backupPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        });
    }
}
"@
    
    # Najít předposlední } (konec třídy)
    $lines = $content -split "`n"
    $newContent = ""
    $foundLastNamespace = $false
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Pokud je to poslední } před koncem souboru (a není to poslední řádek)
        if ($line -match "^\s*}$" -and $i -ge $lines.Count - 10 -and -not $foundLastNamespace) {
            $newContent += "    public async Task DeleteBackupAsync(string backupPath)`r`n"
            $newContent += "    {`r`n"
            $newContent += "        await Task.Run(() =>`r`n"
            $newContent += "        {`r`n"
            $newContent += "            if (File.Exists(backupPath))`r`n"
            $newContent += "            {`r`n"
            $newContent += "                File.Delete(backupPath);`r`n"
            $newContent += "            }`r`n"
            $newContent += "        });`r`n"
            $newContent += "    }`r`n"
            $newContent += "}`r`n"
            $foundLastNamespace = $true
        } else {
            $newContent += $line + "`n"
        }
    }
    
    [System.IO.File]::WriteAllText($destPath, $newContent, $utf8)
    Write-Output "BackupService.cs - DeleteBackupAsync added"
}

Write-Output "Done"