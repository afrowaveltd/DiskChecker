# DiskChecker Project Structure

## Overview

DiskChecker is a modular .NET application with clean architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                    UI Layer (3 apps)                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐       │
│  │   Console   │  │     WPF     │  │  Avalonia   │       │
│  │   (CLI)     │  │  (Windows)  │  │ (Cross-platform)│   │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘       │
└─────────┼────────────────┼────────────────┼───────────────┘
          │                │                │
          ▼                ▼                ▼
┌─────────────────────────────────────────────────────────────┐
│              Application Services Layer                    │
│  DiskChecker.Application/                                  │
│  - SmartCheckService, HistoryService                       │
│  - SurfaceTestService, TestReportAnalysisService           │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   Infrastructure Layer                     │
│  DiskChecker.Infrastructure/                               │
│  ├── Hardware/          # Platform-specific SMART providers│
│  │   ├── WindowsSmartaProvider                             │
│  │   └── LinuxSmartaProvider                               │
│  └── Persistence/       # SQLite database                  │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                     Core Layer                              │
│  DiskChecker.Core/                                          │
│  ├── Interfaces/     # ISmartService, ISurfaceTestService  │
│  ├── Models/         # DiskInfo, SmartaData, TestReport    │
│  └── Services/       # QualityCalculator                   │
└─────────────────────────────────────────────────────────────┘
```

## Directory Details

### Root Level

| File/Directory | Description |
|---------------|-------------|
| `build.ps1` | Windows build script (PowerShell) |
| `build.sh` | Linux/macOS build script (Bash) |
| `build-installer.ps1` | Installer builder script |
| `README.md` | Project documentation |
| `DiskChecker.db` | Shared SQLite database |
| `DiskChecker.slnx` | Solution file |

### Core Projects

| Project | Description |
|---------|-------------|
| `DiskChecker.Core/` | Models, interfaces, core services |
| `DiskChecker.Application/` | Business logic, use cases |
| `DiskChecker.Infrastructure/` | Platform implementations, persistence |

### UI Projects

| Project | Framework | Target |
|---------|-----------|--------|
| `DiskChecker.UI/` | Spectre.Console | Terminal/CLI |
| `DiskChecker.UI.WPF/` | WPF | Windows Desktop |
| `DiskChecker.UI.Avalonia/` | Avalonia | Cross-platform |

### Additional

| Directory | Description |
|-----------|-------------|
| `installer/` | Installer configurations (Inno Setup, DEB, RPM) |
| `docs/` | Documentation files |
| `scripts/` | Build and utility scripts |
| `DiskChecker.Tests/` | Unit tests |
| `DiskChecker.Tests.WPF/` | WPF integration tests |

## Shared Components

### Database

All UI applications share the same SQLite database:
- **Location**: `DiskChecker.db` (root directory)
- **Tables**: Tests, Reports, Settings
- **Access**: Via `DiskChecker.Infrastructure.Persistence`

### Models

Core models in `DiskChecker.Core/Models/`:
- `DiskInfo` - Basic disk information
- `SmartaData` - SMART attributes
- `SurfaceTestResult` - Surface test results
- `TestReport` - Generated reports
- `HistoricalTest` - Test history records

### Services

Common services in `DiskChecker.Application/Services/`:
- `SmartCheckService` - SMART data reading
- `HistoryService` - Test history management
- `SurfaceTestService` - Disk surface testing
- `TestReportAnalysisService` - Result analysis
- `ReportEmailService` - Email report delivery
