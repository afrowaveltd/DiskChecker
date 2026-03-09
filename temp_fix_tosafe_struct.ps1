$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit strukturu ToSafeString metod
$oldPattern = @"    public static string ToSafeString\(this int value\)

    // Extension for any object - returns ToString or empty string
    public static string ToSafeString\(this object\? value\)
    \{
        return value\?\.ToString\(\) \?\? string\.Empty;
    \}
    \{
        return value\.ToString\(\);
    \}"

$newPattern = @"    // Přetížení pro ne-nullable int
    public static string ToSafeString(this int value)
    {
        return value.ToString();
    }
    
    // Extension for any object - returns ToString or empty string
    public static string ToSafeString(this object? value)
    {
        return value?.ToString() ?? string.Empty;
    }"

$content = $content -replace $oldPattern, $newPattern

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "ToSafeString methods structure fixed"