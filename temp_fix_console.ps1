$utf8 = New-Object System.Text.UTF8Encoding $false

# Opravit LiveSmartDisplay.cs
$filePath = "D:\DiskChecker\DiskChecker.UI\Console\LiveSmartDisplay.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# int? -> string konverze - použít .ToString() nebo "??" 
$content = $content -replace '\(int\?\)attr\.Value\)', 'attr.Value?.ToString() ?? "0"'
$content = $content -replace 'attr\.Value\)\.PadLeft', '(attr.Value?.ToString() ?? "0").PadLeft'
$content = $content -replace 'attr\.Worst\)\.PadLeft', '(attr.Worst?.ToString() ?? "0").PadLeft'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "LiveSmartDisplay.cs updated"

# Opravit DiagnosticsApp.cs
$filePath = "D:\DiskChecker\DiskChecker.UI\Console\DiagnosticsApp.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# int? ?? string - použít .ToString() 
$content = $content -replace 'powerOnHours \?\? "N/A"', 'powerOnHours?.ToString() ?? "N/A"'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "DiagnosticsApp.cs updated"

# Opravit MainConsoleMenu.cs
$filePath = "D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# int? -> string konverze
$content = $content -replace 'attr\.Value\)\.PadLeft\(8\)', '(attr.Value?.ToString() ?? "0").PadLeft(8)'
$content = $content -replace 'attr\.Worst\)\.PadLeft\(8\)', '(attr.Worst?.ToString() ?? "0").PadLeft(8)'

# int? -> int konverze
$content = $content -replace 'int hoursCount = result\.SmartaData\.PowerOnHours;', 'int hoursCount = result.SmartaData.PowerOnHours ?? 0;'

# PagedResult.PageIndex neexistuje - odstranit nebo změnit
$content = $content -replace 'pagedResult\.PageIndex', 'pagedResult.Page'

# int.Count neexistuje
$content = $content -replace 'hoursCount\.Count', 'hoursCount'

# Markup.Escape null handling
$content = $content -replace 'Markup\.Escape\(.*?\?\)', 'Markup.Escape($1 ?? "")'.Replace('$1', 'value?.ToString() ?? "0"')

# CoreDriveInfo -> DriveInfo konverze
$content = $content -replace 'System\.IO\.DriveInfo driveInfo = drive;', 'var driveInfo = drive;' 

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "MainConsoleMenu.cs updated"