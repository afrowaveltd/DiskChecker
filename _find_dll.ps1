$pkg = "$env:USERPROFILE\.nuget\packages\oxyplot.avalonia_11_1"
if (Test-Path $pkg) {
    Get-ChildItem -Path $pkg -Recurse -Filter '*.dll' | Select-Object -First 10 | ForEach-Object { Write-Host $_.Name }
} else {
    Write-Host "Package not found at $pkg"
}
