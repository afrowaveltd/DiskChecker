# DiskChecker

Cross-platform disk diagnostics and SMART analysis tool with beautiful UI.

## Features

- **SMART Data Analysis** - Read and analyze SMART data from HDD/SSD
- **Surface Testing** - Comprehensive disk performance and reliability testing
- **Quality Ratings** - A-F grading system based on SMART attributes
- **Multi-Platform UI** - Windows (WPF, Avalonia), Linux (Avalonia), Terminal
- **Export Options** - PDF, CSV, JSON reports
- **Email Notifications** - Send test reports via email

## Supported Platforms

| Platform | UI Framework | Status |
|----------|--------------|--------|
| Windows 10/11 | WPF | ✅ Stable |
| Windows 10/11 | Avalonia | ✅ Stable |
| Linux (x64) | Avalonia | ✅ Stable |
| Terminal | Spectre.Console | ✅ Stable |

## Requirements

- .NET 10.0 Runtime (or self-contained builds)
- Administrator/root privileges for SMART access
- Linux: smartctl (smartmontools package)

## Quick Start

### Build from Source

```powershell
# Windows - Build all applications
.\build.ps1 -Configuration Release -Platform All

# Linux / macOS
./build.sh
```

### Running the Application

```powershell
# Console Application
.\publish\console\DiskChecker.UI.exe

# WPF (Windows)
.\publish\wpf\DiskChecker.UI.WPF.exe

# Avalonia (Windows)
.\publish\avalonia-win\DiskChecker.UI.Avalonia.exe

# Avalonia (Linux)
./publish/avalonia-linux-x64/DiskChecker.UI.Avalonia
```

## Project Structure

```
DiskChecker/
├── DiskChecker.Core/          # Core models and interfaces
├── DiskChecker.Application/   # Business logic services
├── DiskChecker.Infrastructure/# Platform-specific implementations
│   └── Hardware/             # SMART providers (Windows, Linux)
├── DiskChecker.UI/            # Console application
├── DiskChecker.UI.WPF/       # WPF desktop application
├── DiskChecker.UI.Avalonia/  # Cross-platform Avalonia app
└── installer/                # Installation packages
```

## Database

All applications share the same SQLite database (`DiskChecker.db`) located in:
- Windows: Application directory or `%APPDATA%\DiskChecker\`
- Linux: Application directory or `~/.config/DiskChecker/`

## Building Installers

```powershell
# Build all installers (requires build.ps1 to run first)
.\build-installer.ps1 -Platform All

# Windows only (requires Inno Setup)
.\build-installer.ps1 -Platform Windows

# Linux only
.\build-installer.ps1 -Platform Linux
```

### Installer Requirements

- **Windows**: [Inno Setup](https://jrsoftware.org/isinfo.php) 6.x
- **Linux DEB**: dpkg-deb
- **Linux RPM**: rpmbuild

## Development

```powershell
# Build and run in development mode
dotnet run --project DiskChecker.UI.Avalonia

# Run tests
dotnet test

# Clean build artifacts
dotnet clean
.\build.ps1 -Clean
```

## Configuration

Edit `DiskChecker.UI/appsettings.json` to configure:
- Email settings for report delivery
- Logging levels
- Database path

## License

MIT License - see LICENSE.txt for details.

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting pull requests.
