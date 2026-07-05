# DiskChecker

**DiskChecker** is a professional desktop application for diagnostics, stress testing, sanitization, and record-keeping of hard drives and SSDs. The project is built on .NET 10 and Avalonia UI, stores history in SQLite, and supports Windows and Linux.

The application is designed for service/testing workflows: quick disk identification, SMART check, surface and seek tests, destructive disk verification, disk card creation, test history, disk comparison, and certificate/report export.

> [!WARNING]
> Some DiskChecker modes are **destructive** and can irreversibly erase all data on the selected disk. Before running sanitization or a destructive test, always verify the selected disk, device path, and backups.

## Current Application State

DiskChecker is more than a simple SMART utility. It includes a complete application layer, persistence, cross-platform desktop UI, platform-specific disk access, and service tools around testing.

### Main Features

- **Disk Detection**
  - Windows: physical disks, volumes, and supplementary information via system APIs/WMI.
  - Linux: block device and volume detection, typically via `/dev/sdX`, `/dev/nvmeXnY`.
  - Disk identity recognition including model, serial number, firmware, capacity, bus type, and connection type.
- **SMART/SMARTA Diagnostics**
  - Reading SMART data through platform providers (Windows and Linux).
  - Advanced attributes, temperatures, self-tests, self-test log, and maintenance actions.
  - SMART data cache with configurable TTL in `appsettings.json`.
  - **Negative cache** – if a disk does not support SMART (USB adapters, RAID controllers), a sentinel is stored in the cache for 30 minutes to prevent repeated timeouts and UI freezing.
  - Instructions and dependency installation attempts where external tools are needed.
- **SMART Historical Trends**
  - Automatic storage of SMART snapshots in a dedicated `SmartSnapshots` table on each test.
  - **Trend analysis** across tests – temperature, reallocated sectors, wear leveling, percentage used, pending sectors, and more.
  - Linear regression to calculate degradation rate and predict days until critical threshold.
  - Visual trend charts directly in the analysis workspace.
- **Vendor-Specific SSD Wear Assessment**
  - Wear_Leveling_Count (ID 177) mapping by manufacturer – Samsung, Intel, Seagate, Western Digital, SanDisk, Crucial/Micron, Kingston, Toshiba/Kioxia, SK Hynix, ADATA, Corsair, and more.
  - Normalized value interpreted as remaining life % (100 = new, 0 = dead).
  - Automatic NVMe device detection using standardized `PercentageUsed`.
  - Human-readable wear status description with color-coded severity.
- **Disk Health Assessment**
  - Score calculation and A–F grade.
  - Evaluation of critical SMART attributes, errors, performance, and test progression.
  - **Adaptive anomaly detection** – two-level speed sampling with performance drop detection, high-resolution recording, and analysis.
  - **AnomalyAnalysisService** – pairing write+read anomalies, correlation calculation, repeating pattern detection, score penalty.
  - Analytical layer for anomaly detection and service recommendations.
- **Surface Test**
  - Profiles for HDD/SSD/NVMe and various operation modes.
  - Speed, temperature, error, and progress sampling.
  - Progress visualization and result storage in history.
- **Seek Test**
  - Seek latency testing with recommended settings based on disk condition.
  - Statistics: average/min/max/median/P95/P99 and error rate.
  - Conservative modes for old or risky disks.
- **Destructive Test / Sanitization**
  - Zero writing, read/verify, and metric collection.
  - Optional partition creation and formatting upon completion.
  - Windows and Linux implementation via `IDiskSanitizationService`.
  - Recovery information, detailed errors, and result recording in test session.
  - **Adaptive sampling** during sanitization – standard samples for charts + high-resolution anomaly recording for later analysis.
- **Absolute Destructive Test**
  - Complete workflow: 2× sanitization (write+read), 3× seek test (full-stroke, random, skip), SMART before/after.
  - Automatic certificate generation with charts, metrics, and anomaly analysis.
  - Detailed report with overlay comparison of write/read anomalies.
- **Safe Destructive Workflow**
  - Separate UI section for safer destructive test execution.
  - Backup before test, destructive test, restore after test.
  - Emphasis on device confirmation and error minimization.
- **Analysis Workspace**
  - Historical test session browser with detailed charts.
  - **Throughput charts** – write/read speed by disk progress and by time.
  - **Seek latency charts** – seek latency by index.
  - **Temperature trends** – temperature evolution during the test.
  - **Anomaly and stall detection** – highlighted regions in charts with zoom capability.
  - **SMART trends** – key metric evolution across tests (temperature, reallocated, wear leveling, % used).
  - **Vendor-specific wear assessment** – Wear_Leveling_Count interpretation by manufacturer.
  - Compact and full display modes, automatic window width adaptation.
- **Disk Cards**
  - Service disk card by serial number.
  - Test session records, SMART snapshots, score, grade, notes, archiving, and locks.
  - Disk detail with history summary and links to certificates/comparison/reports.
- **Certificates and Reports**
  - Certificate generation with charts, metrics, and anomaly analysis.
  - **Cross-platform** – PDF and JPEG preview via SkiaSharp (Windows, Linux, macOS).
  - Label generation (PNG).
  - PDF/export workflow and certificate browser.
  - Complete reports, preview, and print/export as implemented in the UI.
- **History and Database**
  - SQLite database `DiskChecker.db`.
  - Legacy test tables and new entities `DiskCards`, `TestSessions`, `DiskCertificates`, `DiskArchives`, `SmartSnapshots`.
  - Schema compatibility patcher on application startup (automatic addition of missing columns).
- **Disk Comparison**
  - Comparison of selected disk cards and their performance/health metrics.
- **Backup and Restore**
  - **Three backup modes**: file-based, RAW image (sectors), VHDx dynamic image.
  - **VHDx** – standard Microsoft format, mountable natively on Windows and via `qemu-nbd` on Linux.
  - **Error resilience** – unreadable sectors are replaced with zeros, backup continues; protection against catastrophic failure (consecutive error limit).
  - **Larger blocks** (1 MiB) for faster transfer.
  - UI for target location selection with free space overview.
  - Restore from backup including verification.
- **Email Notifications**
  - SMTP settings storage.
  - Report and notification sending after test completion.
- **Localization**
  - Localization files `cs.json` and `en.json` are in the repository.
  - Dynamic language switching at runtime.

## Projects in Solution

| Project | Role |
| --- | --- |
| `DiskChecker.Core` | Domain models, interfaces, and core services. Contains e.g. `CoreDriveInfo`, `SmartaData`, `DiskCard`, `TestSession`, `DiskCertificate`, `SpeedAnomaly`, `SurfaceTestResult`, `SeekTestResult`, `AdaptiveSpeedSampler`, `AnomalyAnalysisService`, `QualityCalculator`, `SmartTrendService`, `VendorWearMapping`. |
| `DiskChecker.Infrastructure` | Platform and technical implementation: SMART providers for Windows/Linux, volume detection, surface/seek executors, sanitization, SQLite persistence, repositories, certificate generator (SkiaSharp), disk comparison, and SchemaCompatibilityPatcher. |
| `DiskChecker.Application` | Application/use-case layer: SMART check, disk cards and test sessions, history, reports, certification, email, settings, notifications, archiving, and test analysis. |
| `DiskChecker.UI.Avalonia` | Main desktop UI in Avalonia + MVVM. Contains view models, views, navigation, dialogs, localization, backup/restore, and DI registration. |
| `DiskChecker.TUI` | Standalone terminal/experimental project outside the main `.slnx` workflow. |
| `tests/DiskChecker.Tests` | Unit tests (190 tests) for adaptive sampling, anomaly analysis, certificates, disk identity, sanitization progress, seek tests, settings, SMART parser/cache, and other components. |

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ DiskChecker.UI.Avalonia                                     │
│ Avalonia 11 · MVVM · CommunityToolkit.Mvvm · Views/VM       │
│ Navigation · dialogs · localization · charts · user flows   │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│ DiskChecker.Application                                      │
│ Use-cases: SmartCheck · Surface/Seek · DiskCards · Reports   │
│ History · Certificates · Email · Settings · Notifications    │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│ DiskChecker.Infrastructure                                   │
│ Windows/Linux SMART · disk detection · sanitization · SQLite │
│ EF Core repository · test executors · certificate services   │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│ DiskChecker.Core                                             │
│ Models · interfaces · AdaptiveSpeedSampler ·                  │
│ AnomalyAnalysisService · QualityCalculator · SmartTrendService│
│ VendorWearMapping · shared contracts                         │
└─────────────────────────────────────────────────────────────┘
```

Detailed technical documentation is in [`docs/ARCHITECTURE_CS.md`](docs/ARCHITECTURE_CS.md).

## Technologies and Main Dependencies

- **.NET 10.0** (`net10.0`)
- **Avalonia 11.3.12**
  - The project intentionally stays on Avalonia 11.3.12 for compatibility with `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5`.
- **CommunityToolkit.Mvvm 8.4.2**
- **Entity Framework Core + SQLite**
- **SkiaSharp 3.119.4** – cross-platform rendering for certificates, charts, and labels
- **LiveChartsCore 2.0.5** – charts in UI (surface test, seek test, sanitization)
- **OxyPlot.Avalonia 2.1.0** – supplementary charts
- **MailKit/MimeKit** for SMTP
- **smartmontools/smartctl** mainly on Linux and for advanced SMART scenarios
- **xUnit v3**, **NSubstitute** for tests

## Requirements

### Development

- .NET 10.0 SDK per `global.json`
- Windows 10+ or a modern Linux distribution
- Git and common build tools for the platform

### Runtime

- Administrator/root privileges for physical disk access.
- Linux: `smartmontools` (`smartctl`) and block device permissions.
- Windows: the application has a manifest requesting administrator privileges.

> [!NOTE]
> Without elevated privileges, some UI features may work, but disk detection, SMART data, seek/surface tests, or sanitization may fail or be incomplete.

## Quick Start for Developers

```bash
# Restore packages
dotnet restore

# Build
dotnet build --configuration Release

# Run the main desktop application
dotnet run --project DiskChecker.UI.Avalonia

# Tests
dotnet test
```

On Windows, the same commands can be run in PowerShell/CMD. For disk operations, run the terminal as Administrator.

## Publishing

Manual publish of the main application:

```bash
# Windows x64
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r win-x64 --self-contained true

# Linux x64
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r linux-x64 --self-contained true

# Linux ARM64
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r linux-arm64 --self-contained true
```

Or use the scripts in the repository root:

```bash
./build.sh      # restore, build, and publish for win-x64/linux-x64/linux-arm64
./package.sh    # create Linux tarball/DEB packages from publish output
```

Linux installation workflow is described in [`docs/LINUX_INSTALL.md`](docs/LINUX_INSTALL.md).

## User Documentation

- [`docs/USER_GUIDE_CS.md`](docs/USER_GUIDE_CS.md) – user guide in Czech.
- [`docs/LINUX_INSTALL.md`](docs/LINUX_INSTALL.md) – installation and troubleshooting on Linux.
- [`docs/ARCHITECTURE_CS.md`](docs/ARCHITECTURE_CS.md) – technical architecture and component map.

## Database and Application Data

The default desktop application registers the SQLite database as:

```text
DiskChecker.db
```

The file is created in the application's working directory. The database contains both legacy tables (`DriveRecords`, `TestRecords`, `SmartaRecords`, `SurfaceTestSamples`) and new service entities for disk cards, sessions, certificates, archiving, SMART snapshots, SMTP settings, and a replication queue.

On application startup, `EnsureCreated()` is called followed by `SchemaCompatibilityPatcher.Apply(...)` to adapt older databases to the current schema. The patcher automatically adds missing columns (e.g., `AnomaliesJson`, `Sanitize1ResultJson`, `SeekResultsJson`) without data loss.

## Configuration

The main desktop project copies `appsettings.json` to the output. Currently, the SMART cache settings are the primary configuration:

```json
{
  "SmartaCacheOptions": {
    "TtlMinutes": 10
  }
}
```

If the configuration is missing or invalid, the application uses a default TTL of 10 minutes.

## Key Features – Detail

### Adaptive Sampling and Anomaly Detection

During sanitization tests, DiskChecker uses a two-level `AdaptiveSpeedSampler`:

- **Standard samples** (~200 points) – for charts and database storage, with time-based decimation.
- **High-resolution anomalies** (100ms intervals) – triggered when speed deviates >15% from the rolling baseline. Uses a frozen baseline (not contaminated by anomalous samples) and 5% hysteresis (prevents flickering).

After test completion, `AnomalyAnalysisService`:
- Pairs write and read anomalies at the same disk position → **overlay comparison**.
- Calculates correlation (0–100) based on position, deviation, duration, and direction → ≥70 = likely physical defect.
- Detects repeating patterns at the same position.
- Generates a human-readable report (part of the certificate).
- Calculates a 0–50 point penalty to the overall disk score.

### SMART Historical Trends

On each test, a SMART snapshot is automatically saved to the `SmartSnapshots` table. The `SmartTrendService` then:

- Aggregates all snapshots for a given disk.
- Calculates linear regression for key metrics (temperature, reallocated sectors, wear leveling, percentage used, pending sectors, uncorrectable errors, media errors, unsafe shutdowns, power-on hours, available spare).
- Computes the rate of change per day and predicts days until critical threshold.
- Generates a human-readable summary report.
- Provides data for trend chart rendering in the analysis workspace.

### Vendor-Specific SSD Wear Assessment

`VendorWearMapping` contains mappings for 20+ SSD manufacturers. Each entry specifies:

- **Normalized value semantics** – most manufacturers use a descending scale 100→0 (100 = new, 0 = dead).
- **Raw value** – average erase count of NAND blocks.
- **Threshold values** for warning (≤30) and critical (≤10) status.

For NVMe drives, the standardized `PercentageUsed` attribute (0–100% wear) is used automatically.

### Backup Modes

| Mode | Description | Use Case |
|------|-------------|---------|
| **File-based** | Copies selected folders and files | Regular data backup |
| **RAW image** | Reads sectors directly from disk (1 MiB blocks) | Complete bit-level disk copy |
| **VHDx dynamic** | Creates a standard VHDx image (Microsoft format) | Mountable on Windows and Linux, grows with data |

All modes are resilient to read errors – unreadable sectors are replaced with zeros and the backup continues. If more than 64 consecutive errors occur, the operation is aborted (protection against catastrophic disk failure).

### SMART Negative Cache

If a disk or adapter does not support SMART, the provider stores a sentinel in the cache for 30 minutes. On subsequent requests, `null` is returned immediately without calling `smartctl` again – the UI does not freeze and tests are not delayed by timeouts.

## Repository Structure

```text
DiskChecker/
├── DiskChecker.Core/              # Domain models, interfaces, AdaptiveSpeedSampler, AnomalyAnalysisService, QualityCalculator, SmartTrendService, VendorWearMapping
├── DiskChecker.Infrastructure/    # Platform implementation, SMART, tests, sanitization, SQLite, SchemaCompatibilityPatcher
├── DiskChecker.Application/       # Application services and business logic
├── DiskChecker.UI.Avalonia/       # Main desktop UI
│   ├── ViewModels/                # MVVM view models (30+ screens)
│   ├── Views/                     # Avalonia AXAML views
│   ├── Services/                  # Navigation, dialogs, backup, localization, document state
│   ├── Converters/                # UI converters
│   ├── Locales/                   # cs/en translations
│   └── Assets/                    # Icons and assets
├── DiskChecker.TUI/               # Terminal/experimental project
├── tests/DiskChecker.Tests/       # Unit tests (190 tests)
├── docs/                          # User and technical documentation
├── installer/                     # Linux/Windows installation resources
├── scripts/                       # Helper build scripts
├── build.sh                       # Cross-runtime publish script
├── package.sh                     # Linux packaging script
└── version.properties             # Version for packaging
```

## Security Notes

- Destructive tests and sanitization work with physical devices, not just files.
- On Linux, the device may be designated e.g. `/dev/sda`; on Windows e.g. `\\.\PhysicalDrive1`.
- Never test the system disk in destructive mode.
- With external USB adapters, SMART may be partially unavailable or distorted.
- Do not disconnect the disk or interrupt power during long tests.
- Before a destructive test, always verify you have a valid data backup.

## Testing

```bash
dotnet test
```

The test project (190 tests) covers:
- Adaptive sampling and anomaly detection (11 tests)
- Anomaly analysis – overlays, correlation, penalties, reports
- Certificate generation
- Disk identity
- Sanitization progress
- Seek tests
- Settings
- SMART parsers and cache
- SMART support detection

## License

See the [`LICENSE`](LICENSE) file.
