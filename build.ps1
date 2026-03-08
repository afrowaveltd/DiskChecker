<#
.SYNOPSIS
    DiskChecker Build Script - Windows PowerShell
    
.DESCRIPTION
    Builds all DiskChecker UI applications (Console, WPF, Avalonia)
    for Windows and Linux platforms.
    
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
    
.PARAMETER Platform
    Target platform: Windows, Linux, or All. Default: All
    
.PARAMETER Clean
    Clean build artifacts before building
    
.EXAMPLE
    .\build.ps1 -Configuration Release -Platform All
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("Windows", "Linux", "All")]
    [string]$Platform = "All",
    
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

# Change to script directory
Set-Location $ScriptDir

Write-Host @"
╔══════════════════════════════════════════════════════════════╗
║              DiskChecker Build Script v1.0                   ║
║              Cross-platform Disk Diagnostics                  ║
╚════════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Magenta

Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platform:      $Platform" -ForegroundColor Yellow

# Clean if requested
if ($Clean) {
    Write-Step "Cleaning build artifacts..."
    dotnet clean --configuration $Configuration --verbosity quiet
    if (Test-Path ".\publish") {
        Remove-Item -Recurse -Force ".\publish"
    }
    Write-Success "Clean completed"
}

# Restore packages
Write-Step "Restoring NuGet packages..."
dotnet restore --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to restore packages"
    exit 1
}
Write-Success "Packages restored"

# Build solution
Write-Step "Building solution..."
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}
Write-Success "Solution built successfully"

# Create publish directory
$PublishDir = ".\publish"
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir | Out-Null
}

# ============ Publish Console Application ============
Write-Step "Publishing Console Application..."

$ConsoleOutput = "$PublishDir\console"
dotnet publish .\DiskChecker.UI\DiskChecker.UI.csproj `
    --configuration $Configuration `
    --no-build `
    --output $ConsoleOutput

if ($LASTEXITCODE -ne 0) {
    Write-Error "Console publish failed"
    exit 1
}
Write-Success "Console: $ConsoleOutput"

# ============ Publish WPF Application (Windows only) ============
if ($Platform -eq "Windows" -or $Platform -eq "All") {
    Write-Step "Publishing WPF Application (Windows)..."
    
    $WpfOutput = "$PublishDir\wpf"
    dotnet publish .\DiskChecker.UI.WPF\DiskChecker.UI.WPF.csproj `
        --configuration $Configuration `
        --no-build `
        --output $WpfOutput
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "WPF publish failed"
        exit 1
    }
    Write-Success "WPF: $WpfOutput"
}

# ============ Publish Avalonia Application ============
if ($Platform -eq "Windows" -or $Platform -eq "All") {
    Write-Step "Publishing Avalonia Application (Windows)..."
    
    $AvaloniaWinOutput = "$PublishDir\avalonia-win"
    dotnet publish .\DiskChecker.UI.Avalonia\DiskChecker.UI.Avalonia.csproj `
        --configuration $Configuration `
        --no-build `
        --output $AvaloniaWinOutput
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Avalonia (Windows) publish failed"
        exit 1
    }
    Write-Success "Avalonia (Windows): $AvaloniaWinOutput"
}

if ($Platform -eq "Linux" -or $Platform -eq "All") {
    Write-Step "Publishing Avalonia Application (Linux)..."
    
    $AvaloniaLinuxOutput = "$PublishDir\avalonia-linux-x64"
    dotnet publish .\DiskChecker.UI.Avalonia\DiskChecker.UI.Avalonia.csproj `
        --configuration $Configuration `
        --runtime linux-x64 `
        --self-contained true `
        --no-build `
        --output $AvaloniaLinuxOutput
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Avalonia (Linux) publish failed"
        exit 1
    }
    Write-Success "Avalonia (Linux): $AvaloniaLinuxOutput"
}

# ============ Summary ============
Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║                     BUILD COMPLETED                          ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Magenta

Write-Host "Published artifacts:" -ForegroundColor Cyan
Get-ChildItem $PublishDir -Directory | ForEach-Object {
    $size = (Get-ChildItem $_.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host "  📁 $($_.Name) ($([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
}

Write-Host "`nTo run the applications:" -ForegroundColor Cyan
if ($Platform -eq "Windows" -or $Platform -eq "All") {
    Write-Host "  Console:  .\publish\console\DiskChecker.UI.exe" -ForegroundColor White
    Write-Host "  WPF:      .\publish\wpf-x64\DiskChecker.UI.WPF.exe" -ForegroundColor White
    Write-Host "  Avalonia: .\publish\avalonia-win-x64\DiskChecker.UI.Avalonia.exe" -ForegroundColor White
}
if ($Platform -eq "Linux" -or $Platform -eq "All") {
    Write-Host "  Avalonia: .\publish\avalonia-linux-x64\DiskChecker.UI.Avalonia" -ForegroundColor White
}

Write-Host ""
