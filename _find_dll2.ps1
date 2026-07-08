$pkg = "$env:USERPROFILE\.nuget\packages\oxyplot.avalonia_11_1\2.2.0"
if (Test-Path $pkg) {
    Get-ChildItem -Path $pkg -Recurse -Filter '*.dll' | Select-Object -First 10 | ForEach-Object { Write-Host $_.Name }
} else {
    Write-Host "Package not found at $pkg"
    # Try to find it
    $found = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Directory -Filter "oxyplot*" -ErrorAction SilentlyContinue
    foreach ($f in $found) {
        Write-Host "Found: $($f.FullName)"
        Get-ChildItem -Path $f.FullName -Directory | ForEach-Object { Write-Host "  Version: $($_.Name)" }
    }
}
