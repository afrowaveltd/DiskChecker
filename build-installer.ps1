<#
.SYNOPSIS
    DiskChecker Installer Build Script
    
.DESCRIPTION
    Builds installation packages for DiskChecker:
    - Windows: Inno Setup (.exe)
    - Linux: DEB and RPM packages
    
.PARAMETER Platform
    Target platform: Windows, Linux, or All
    
.EXAMPLE
    .\build-installer.ps1 -Platform All
#>

param(
    [ValidateSet("Windows", "Linux", "All")]
    [string]$Platform = "All"
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

Set-Location $ScriptDir

Write-Host @"
╔══════════════════════════════════════════════════════════════╗
║         DiskChecker Installer Builder v1.0                  ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Magenta

# Check if publish exists
if (-not (Test-Path ".\publish")) {
    Write-Error "Published files not found. Run build.ps1 first!"
    exit 1
}

# Create installer output directory
$InstallerDir = ".\installer"
if (-not (Test-Path $InstallerDir)) {
    New-Item -ItemType Directory -Path $InstallerDir | Out-Null
}

# ============ Windows Installer ============
if ($Platform -eq "Windows" -or $Platform -eq "All") {
    Write-Step "Building Windows Installer (Inno Setup)..."
    
    # Check if Inno Setup is installed
    $innoPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $innoPath)) {
        $innoPath = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    }
    
    if (Test-Path $innoPath) {
        & $innoPath "$InstallerDir\DiskChecker.iss"
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Windows installer created: $InstallerDir\DiskChecker-1.0.0-Setup.exe"
        } else {
            Write-Error "Inno Setup compilation failed"
        }
    } else {
        Write-Host "⚠️  Inno Setup not found. Skipping Windows installer." -ForegroundColor Yellow
        Write-Host "   Install Inno Setup from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    }
}

# ============ Linux DEB Package ============
if ($Platform -eq "Linux" -or $Platform -eq "All") {
    Write-Step "Building Linux DEB Package..."
    
    try {
        $DebOutput = "$InstallerDir/diskchecker_1.0.0_amd64.deb"
        
        # Create temporary build directory
        $BuildDir = "$InstallerDir/deb_build"
        if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
        New-Item -ItemType Directory -Path "$BuildDir/usr/share/diskchecker" | Out-Null
        New-Item -ItemType Directory -Path "$BuildDir/DEBIAN" | Out-Null
        
        # Copy files
        Copy-Item "publish\avalonia-linux-x64\*" "$BuildDir\usr\share\diskchecker\" -Recurse
        
        # Copy control files
        Copy-Item "$InstallerDir\diskchecker\DEBIAN\control" "$BuildDir\DEBIAN\"
        
        # Build DEB
        dpkg-deb --build "$BuildDir" $DebOutput 2>$null
        if (Test-Path $DebOutput) {
            Write-Success "DEB package created: $DebOutput"
        }
        
        # Cleanup
        Remove-Item -Recurse -Force $BuildDir
    }
    catch {
        Write-Error "DEB build failed: $_"
    }
}

# ============ Linux RPM Package ============
if ($Platform -eq "Linux" -or $Platform -eq "All") {
    Write-Step "Building Linux RPM Package..."
    
    try {
        $RpmOutput = "$InstallerDir/diskchecker-1.0.0-1.x86_64.rpm"
        
        # Check if rpmbuild is available
        $rpmbuild = Get-Command rpmbuild -ErrorAction SilentlyContinue
        if (-not $rpmbuild) {
            Write-Host "⚠️  rpmbuild not found. Skipping RPM package." -ForegroundColor Yellow
            Write-Host "   Install rpmdevtools on Fedora/RHEL: sudo dnf install rpmdevtools" -ForegroundColor Yellow
        }
        else {
            # Create RPM build directory
            $RpmBuildRoot = "$env:TEMP\diskchecker_rpm"
            if (Test-Path $RpmBuildRoot) { Remove-Item -Recurse -Force $RpmBuildRoot }
            
            New-Item -ItemType Directory -Path "$RpmBuildRoot\BUILD" | Out-Null
            New-Item -ItemType Directory -Path "$RpmBuildRoot\BUILDROOT" | Out-Null
            New-Item -ItemType Directory -Path "$RpmBuildRoot\RPMS" | Out-Null
            New-Item -ItemType Directory -Path "$RpmBuildRoot\SOURCES" | Out-Null
            New-Item -ItemType Directory -Path "$RpmBuildRoot\SPECS" | Out-Null
            New-Item -ItemType Directory -Path "$RpmBuildRoot\srpm" | Out-Null
            
            # Copy source
            Copy-Item "publish\avalonia-linux-x64" "$RpmBuildRoot\SOURCES\diskchecker-1.0.0" -Recurse
            
            # Copy spec file
            Copy-Item "$InstallerDir\diskchecker.spec" "$RpmBuildRoot\SPECS\"
            
            # Build RPM
            Push-Location $RpmBuildRoot
            rpmbuild -bb SPECS/diskchecker.spec --define "_topdir $RpmBuildRoot" 2>$null
            Pop-Location
            
            $rpmFile = Get-ChildItem "$RpmBuildRoot\RPMS\x86_64\*.rpm" -ErrorAction SilentlyContinue
            if ($rpmFile) {
                Copy-Item $rpmFile.FullName $RpmOutput -Force
                Write-Success "RPM package created: $RpmOutput"
            }
            
            # Cleanup
            Remove-Item -Recurse -Force $RpmBuildRoot
        }
    }
    catch {
        Write-Error "RPM build failed: $_"
    }
}

# ============ Summary ============
Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║                  INSTALLER BUILD COMPLETE                   ║
╚══════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Magenta

Write-Host "Generated installers:" -ForegroundColor Cyan
Get-ChildItem "$InstallerDir\*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
    $size = $_.Length / 1MB
    Write-Host "  📦 $($_.Name) ($([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
}
Get-ChildItem "$InstallerDir\*.deb" -ErrorAction SilentlyContinue | ForEach-Object {
    $size = $_.Length / 1MB
    Write-Host "  📦 $($_.Name) ($([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
}
Get-ChildItem "$InstallerDir\*.rpm" -ErrorAction SilentlyContinue | ForEach-Object {
    $size = $_.Length / 1MB
    Write-Host "  📦 $($_.Name) ($([math]::Round($size, 2)) MB)" -ForegroundColor Yellow
}

Write-Host ""
