$found = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages" -Directory -Filter "oxyplot*" -ErrorAction SilentlyContinue
foreach ($f in $found) {
    Write-Host "Found: $($f.FullName)"
    Get-ChildItem -Path $f.FullName -Directory | ForEach-Object { Write-Host "  Version: $($_.Name)" }
    # Also check for DLLs
    Get-ChildItem -Path $f.FullName -Recurse -Filter '*.dll' -ErrorAction SilentlyContinue | Select-Object -First 5 | ForEach-Object { Write-Host "  DLL: $($_.FullName)" }
}
# Also check the global packages folder
$globalPackages = "$env:USERPROFILE\.nuget\packages"
Write-Host "`nAll oxyplot dirs:"
Get-ChildItem -Path $globalPackages -Directory -Filter "oxyplot*" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.Name)" }
