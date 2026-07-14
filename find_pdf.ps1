$encoding = [System.Text.Encoding]::UTF8
$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, $encoding)
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'public.*Generate|async.*Generate|private.*Generate|SaveChart|SaveTo|\.pdf|\.png|QuestPDF|SkiaSharp|Image\.|Bitmap|Canvas|FileStream|Path\.GetTemp|SharpCompress|SevenZip|ZipArchive|new\s+MemoryStream') {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i].Trim())
    }
}
