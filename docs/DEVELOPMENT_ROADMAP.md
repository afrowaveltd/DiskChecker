# DiskChecker Development Roadmap

**Last Updated:** 2025-02-26  
**Status:** Phase 1 Complete - Console UI Functional  
**Next Phase:** Linux Implementation  

---

## 🎯 Project Overview

DiskChecker is a comprehensive disk diagnostics and testing tool with SMART monitoring, surface testing, and comprehensive reporting capabilities.

**Current Implementation:** Windows Console UI (.NET 10, C# 14)  
**Repository:** https://github.com/afrowaveltd/DiskChecker

---

## ✅ Phase 1: Completed (Console UI - Windows)

### Architecture
- **Core Layer:** `DiskChecker.Core` - Domain models, interfaces, business logic
- **Infrastructure:** `DiskChecker.Infrastructure` - Hardware access (diskpart, smartctl), persistence
- **Application:** `DiskChecker.Application` - Services, business logic orchestration
- **UI:** `DiskChecker.UI` - Console interface with Spectre.Console
- **Web:** `DiskChecker.Web` - Blazor Server web interface (in progress)
- **Tests:** `DiskChecker.Tests` - xUnit + NSubstitute test suite

### Key Features Implemented
✅ **SMART Diagnostics**
- Windows: smartctl via `WindowsSmartaProvider`
- Drive health grading (A-F scale)
- Attribute monitoring
- Predictive failure detection

✅ **Disk Listing & Detection**
- Physical disk enumeration
- Mounted volume detection
- Size calculation (TotalSize, FreeSpace)

✅ **Surface Testing (FullDiskSanitization)**
- Admin privilege verification
- SMART pre-check (FAIL/PREFAIL detection)
- Diskpart automation:
  - Partition cleanup (`clean`)
  - NTFS creation with unique label (DISKTEST_yyMMdd_xxxxxxxx)
  - Automatic drive letter assignment
- **Delta Method** for drive detection:
  - Compare available drives before/after formatting
  - 3-retry mechanism with 1s delays
  - 100% reliable detection
- User fallback (mounted drive selection):
  - DriveInfo enumeration with ready filter
  - Non-system drive filtering
  - DISKTEST label prioritization
- Sequential test execution:
  - 100MB file chunks with headers
  - Zero-fill write pattern
  - Data verification (checksum validation)
- Comprehensive progress reporting:
  - Percentage complete
  - Current throughput (MB/s)
  - ETA calculation
  - Sample recording (offset, speed, timestamp)

✅ **Security & Safety**
- System drive protection (never format C:\)
- Admin rights enforcement
- SMART FAIL/PREFAIL warnings
- Confirmation dialogs
- Safe file cleanup

✅ **Console UI**
- Spectre.Console for formatted output
- Interactive menus (disk selection, test profiles)
- Progress bars with ETA
- Formatted result tables
- Color-coded warnings/errors

### Technical Highlights
- **XML Documentation** on all public APIs
- **Unit Tests** with xUnit + NSubstitute
- **Error Handling** with detailed diagnostics
- **Async/Await** throughout
- **CancellationToken** support
- **Platform Detection** (Windows-specific attributes)

---

## 🔄 Phase 2: Linux Implementation

### Objectives
- Port core diagnostics to Linux
- Maintain compatible API/data models
- Support Linux disk tools

### Implementation Plan

#### 2.1 SMART Provider for Linux
**File:** `DiskChecker.Infrastructure/Hardware/LinuxSmartaProvider.cs` (STARTED)

```csharp
// Already partially implemented
public class LinuxSmartaProvider : ISmartaProvider
{
    // Uses smartctl --json on Linux
    // Handles /dev/sdX device paths
}
```

**Tasks:**
- [ ] Complete JSON parsing for Linux smartctl output
- [ ] Handle device discovery (`lsblk`, `smartctl --scan`)
- [ ] Add nvme support
- [ ] Unit tests for common cases
- [ ] Integration tests on actual Linux

#### 2.2 Disk Listing for Linux
**File:** `DiskChecker.Infrastructure/Hardware/LinuxDiskProvider.cs` (NEW)

**Tasks:**
- [ ] Implement `lsblk --json` parsing
- [ ] Extract: device, size, mountpoint, fstype
- [ ] Filter out system partitions (/boot, /root, etc.)
- [ ] Handle NVMe drives (/dev/nvme0n1)
- [ ] Support for LVM volumes

#### 2.3 Surface Test for Linux
**File:** `DiskChecker.Infrastructure/Hardware/LinuxSequentialTestExecutor.cs` (NEW)

**Key Differences:**
- Replace diskpart with `parted`:
  ```bash
  parted -s /dev/sdX mklabel msdos
  parted -s /dev/sdX mkpart primary 0% 100%
  mkfs.ntfs -F /dev/sdX1 -L DISKTEST_...
  ```
- Mount at `/mnt/disktest_<uuid>/` (no drive letters)
- Require `sudo` for privileged operations
- Use `udev` for device hotplug detection

**Tasks:**
- [ ] Diskpart equivalent shell command builder
- [ ] Permission elevation handler (`sudo`, PolicyKit)
- [ ] Device permission checker
- [ ] Mount/unmount automation
- [ ] Error handling for busy devices

#### 2.4 Cross-Platform Abstraction
**File:** `DiskChecker.Core/PlatformAbstraction.cs` (NEW)

```csharp
public interface IPlatformService
{
    PlatformType CurrentPlatform { get; }
    bool IsRunningAsAdmin();
    Task<string> ExecuteCommandAsync(string command, string args);
    // etc.
}

// Implementations:
// - WindowsPlatformService
// - LinuxPlatformService
```

**Tasks:**
- [ ] Detect OS (RuntimeInformation.IsOSPlatform)
- [ ] Implement platform-specific commands
- [ ] Consistent error handling across platforms
- [ ] Tests for each platform

#### 2.5 Console UI Updates
**File:** `DiskChecker.UI/Console/MainConsoleMenu.cs`

**Tasks:**
- [ ] Detect platform on startup
- [ ] Show platform-appropriate device paths
- [ ] Handle permission prompts (sudo on Linux)
- [ ] Adapt progress display for terminal differences

### Testing Strategy
```bash
# Linux test environment setup
- Ubuntu 22.04 LTS VM with:
  - Virtual USB drive (loop device)
  - smartctl installed
  - parted/fdisk available
  - Full permission testing (as user, with sudo)

# Test Cases:
- Device discovery (sda, sdb, nvme0n1)
- SMART data parsing
- Disk formatting and mount
- Sequential write/verify
- Permission elevation
- Cleanup after test
```

---

## 🌐 Phase 3: Web UI (Blazor Server)

### Current Status
- Basic Blazor project structure
- App.razor, MainLayout.razor configured
- Program.cs with Blazor services

### Objectives
- Real-time test monitoring via WebSocket
- Test history visualization
- Device dashboard
- Report generation UI

### Architecture

#### 3.1 Backend Modifications
**Tasks:**
- [ ] Create `DiskCheckerHub` (SignalR hub for real-time updates)
- [ ] Add IProgress<SurfaceTestProgress> → Hub broadcast
- [ ] Create API controllers (devices, tests, reports)
- [ ] Database schema for test history
- [ ] Background task for monitoring

#### 3.2 UI Components
**Files to Create:**
- `DiskChecker.Web/Pages/Dashboard.razor` - Device list, health overview
- `DiskChecker.Web/Pages/Testing.razor` - Start test, real-time progress
- `DiskChecker.Web/Pages/History.razor` - Past tests, comparison
- `DiskChecker.Web/Components/DeviceCard.razor` - Device summary
- `DiskChecker.Web/Components/TestProgress.razor` - Real-time graph

**Technologies:**
- Blazor Server with SignalR
- Chart.js for graphs
- Bootstrap for layout
- Real-time updates via WebSocket

#### 3.3 Security
- [ ] Authentication (local/AD)
- [ ] Authorization for privileged operations
- [ ] Session management
- [ ] CSRF protection
- [ ] Rate limiting

**Tasks:**
- [ ] Implement auth middleware
- [ ] Role-based access (Admin only for testing)
- [ ] Audit logging
- [ ] HTTPS enforcement

---

## 🎨 Phase 4: Avalonia Cross-Platform GUI

### Objectives
- Native UI on Windows + Linux
- Replaces both Console UI and Web for desktop
- Better UX than console
- Avalonia framework (XAML-based)

### Architecture

#### 4.1 Project Setup
```
DiskChecker.Desktop/
├── Views/
│   ├── MainWindow.axaml
│   ├── DashboardView.axaml
│   ├── TestingView.axaml
│   └── HistoryView.axaml
├── ViewModels/
│   ├── MainWindowViewModel.cs
│   ├── DashboardViewModel.cs
│   └── TestingViewModel.cs
├── Models/
│   └── (shared from Core)
└── App.axaml
```

#### 4.2 Implementation Plan
**Technologies:**
- Avalonia UI (XAML)
- MVVM pattern
- Reactive Extensions (Rx)
- Dependency injection

**Key Features:**
- [ ] Device browser with real-time status
- [ ] Test execution with live progress graph
- [ ] Test history with comparison tools
- [ ] SMART data visualization
- [ ] Export reports (PDF/CSV)
- [ ] Dark/light theme support
- [ ] Auto-updates mechanism

#### 4.3 Platform-Specific Features
**Windows:**
- [ ] SystemTray integration
- [ ] Scheduled testing
- [ ] Windows notifications
- [ ] WinForms interop for admin elevation

**Linux:**
- [ ] Freedesktop integration
- [ ] systemd service control
- [ ] Wayland support
- [ ] Desktop entry file

---

## 📦 Phase 5: Installation & Deployment

### Objectives
- Single installer for Windows + Linux
- Auto-install dependencies (smartctl)
- Service registration
- Privilege elevation
- Easy uninstall

### Implementation Plan

#### 5.1 Windows Installer
**Tool:** NSIS (Nullsoft Scriptable Install System)

**Features:**
- [ ] Download smartctl binary from chipset vendor
- [ ] Register as Windows Service (optional)
- [ ] Create Start Menu shortcuts
- [ ] System tray app launcher
- [ ] Uninstall cleanup
- [ ] Required .NET 10 runtime check

**Script:** `installer/windows-setup.nsi`

#### 5.2 Linux Package
**Format:** AppImage + native packages (DEB, RPM)

**Features:**
- [ ] AppImage with bundled smartctl
- [ ] systemd service unit
- [ ] Desktop entry for menu
- [ ] Package managers: apt, dnf
- [ ] Auto-updates via GitHub releases

**Scripts:**
- `installer/linux-build-appimage.sh`
- `installer/disk-checker.service`
- `installer/disk-checker.desktop`

#### 5.3 First-Run Experience
**Tasks:**
- [ ] Permission checker (admin/sudo)
- [ ] smartctl availability check
- [ ] Device enumeration wizard
- [ ] Database initialization
- [ ] Configuration wizard (email, paths)

#### 5.4 Auto-Update Mechanism
**Tasks:**
- [ ] GitHub Releases API integration
- [ ] Background update checker
- [ ] Safe rollback on failure
- [ ] User notification UI
- [ ] Seamless restart handling

---

## 🔧 Technical Debt & Improvements

### High Priority
- [ ] Complete test coverage (target: >80%)
- [ ] XML documentation on all public APIs
- [ ] Performance profiling (async I/O, memory)
- [ ] Error message localization (CZ, EN)

### Medium Priority
- [ ] Database migration framework
- [ ] Configuration versioning
- [ ] Plugin architecture for custom tests
- [ ] REST API for remote monitoring

### Low Priority
- [ ] Internationalization (10+ languages)
- [ ] Mobile companion app
- [ ] Cloud sync for test results
- [ ] Integration with fleet management tools

---

## 📊 Known Issues & Limitations

### Phase 1 (Console)
1. **Admin Elevation:** Must manually run as admin - could auto-elevate with manifest
2. **Drive Letter Detection:** Works 100% now with delta method, but could add VDS API backup
3. **Temp File Cleanup:** No automatic cleanup of failed test files yet
4. **Progress Accuracy:** ETA assumes constant throughput (doesn't account for speed variations)

### Limitations
- Windows-only for now (Phase 2 adds Linux)
- No network/remote disk support
- No scheduled testing yet (Phase 4 feature)
- Limited to local user accounts

---

## 🚀 Getting Started on New Machine

### Prerequisites
```bash
# Windows
- Visual Studio 2022+ or VS Code
- .NET 10 SDK
- Git
- PowerShell 7+

# Linux
- Ubuntu 22.04+ / Fedora 38+
- dotnet 10 SDK
- smartctl (sudo apt install smartmontools)
- parted/fdisk
- Git
- Bash
```

### First Run
```bash
# Clone repository
git clone https://github.com/afrowaveltd/DiskChecker.git
cd DiskChecker

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run console UI (Windows)
cd DiskChecker.UI
dotnet run -- --admin

# Run tests
dotnet test

# Run web (experimental)
cd ../DiskChecker.Web
dotnet run
# Browse to https://localhost:5001
```

### Accessing This Roadmap
This file is your north star! It contains:
- ✅ What's done (Phase 1)
- 📋 What's next (Phase 2-5)
- 🔧 Technical approach
- 📚 File locations
- ⚠️ Known issues

When continuing development:
1. Check current phase
2. Read implementation plan
3. Pick next task
4. Update status here
5. Commit & push

---

## 📞 Key Contacts & Resources

**Repository:** https://github.com/afrowaveltd/DiskChecker  
**Issues:** GitHub Issues (use labels: Phase2-Linux, Phase3-Web, etc.)  
**Documentation:** `/docs/` folder  

**Core Files Reference:**
```
Domain Models:
- DiskChecker.Core/Models/SurfaceTestModels.cs
- DiskChecker.Core/Models/SmartaData.cs

Interfaces:
- DiskChecker.Core/Interfaces/ISurfaceTestExecutor.cs
- DiskChecker.Core/Interfaces/ISmartaProvider.cs

Implementations:
- DiskChecker.Infrastructure/Hardware/SequentialFileTestExecutor.cs
- DiskChecker.Infrastructure/Hardware/WindowsSmartaProvider.cs
- DiskChecker.Infrastructure/Hardware/LinuxSmartaProvider.cs (partial)

Services:
- DiskChecker.Application/Services/SurfaceTestService.cs
- DiskChecker.Application/Services/SmartCheckService.cs

Console UI:
- DiskChecker.UI/Console/MainConsoleMenu.cs
- DiskChecker.UI/Console/DiskCheckerApp.cs
```

---

## 📝 Change Log

| Date | Phase | Status | Notes |
|------|-------|--------|-------|
| 2025-02-26 | 1 | ✅ Complete | Console UI: FullDiskSanitization with delta method, SMART check, disk selection fallback |
| TBD | 2 | 📋 Planned | Linux: smartctl parsing, parted automation, cross-platform abstraction |
| TBD | 3 | 📋 Planned | Web: Blazor UI, SignalR progress, history visualization |
| TBD | 4 | 📋 Planned | Avalonia: Cross-platform GUI for Windows + Linux |
| TBD | 5 | 📋 Planned | Installation: NSIS, AppImage, systemd, auto-updates |

---

**Happy coding! 🚀**
