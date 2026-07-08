$file = 'D:\DiskChecker\DiskChecker.Core\Models\DiskCertificate.cs'
$content = [System.IO.File]::ReadAllText($file)

# Fix SmartAttributeSummary - add AttributeId property (using CRLF)
$old = "public class SmartAttributeSummary`r`n{`r`n    public int Id { get; set; }`r`n    public string Name { get; set; } = string.Empty;`r`n    public string Value { get; set; } = string.Empty;`r`n    public string Status { get; set; } = string.Empty;`r`n    public bool IsCritical { get; set; }`r`n}"

$new = "public class SmartAttributeSummary`r`n{`r`n    public int Id { get; set; }`r`n    public int AttributeId { get; set; }`r`n    public string Name { get; set; } = string.Empty;`r`n    public string Value { get; set; } = string.Empty;`r`n    public string Status { get; set; } = string.Empty;`r`n    public bool IsCritical { get; set; }`r`n}"

if ($content.Contains($old)) {
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content)
    Write-Output "SUCCESS: SmartAttributeSummary fixed"
} else {
    Write-Output "FAILED: Old block not found"
    $idx = $content.IndexOf('class SmartAttributeSummary')
    if ($idx -ge 0) {
        $end = $content.IndexOf('}', $idx + 200)
        Write-Output $content.Substring($idx, $end - $idx + 1)
    }
}
