# DiskChecker - Architektura

Tento dokument popisuje architekturu aplikace DiskChecker.

## Přehled

DiskChecker je desktopová aplikace pro kontrolu zdraví disků pomocí SMART dat a testování povrchu. Aplikace je postavena na .NET 10.0 s Avalonia UI frameworkem.

## Vrstvená architektura

### 1. Core Layer (DiskChecker.Core)

Základní vrstva obsahující modely a rozhraní.

**Modely:**
- `CoreDriveInfo` - Informace o disku
- `SmartaData` - SMART data z disku
- `QualityRating` - Hodnocení zdraví disku (A-F)
- `SurfaceTestResult` - Výsledek testu povrchu
- `HistoricalTest` - Historický záznam testu

**Rozhraní:**
- `ISmartaProvider` - Poskytovatel SMART dat
- `IQualityCalculator` - Kalkulátor kvality disku
- `IDiskDetectionService` - Služba pro detekci disků
- `ISurfaceTestExecutor` - Vykonavatel testu povrchu

**Rozšíření:**
- `IServiceCollectionExtensions` - Registrace služeb do DI

### 2. Infrastructure Layer (DiskChecker.Infrastructure)

Implementační vrstva obsahující konkrétní implementace.

**Hardware:**
- `WindowsSmartaProvider` - Implementace pro Windows (používá smartctl)
- `LinuxSmartaProvider` - Implementace pro Linux
- `SurfaceTestExecutor` - Vykonavatel testu povrchu
- `SmartctlJsonParser` - Parser JSON výstupu smartctl

**Persistence:**
- `DiskCheckerDbContext` - Entity Framework SQLite kontext
- `DriveRecord`, `TestRecord` - Databázové záznamy

### 3. Application Layer (DiskChecker.Application)

Aplikační vrstva obsahující obchodní logiku.

**Služby:**
- `SmartCheckService` - Služba pro SMART kontrolu
- `SurfaceTestService` - Služba pro testování povrchu
- `HistoryService` - Služba pro historii testů
- `SettingsService` - Služba pro nastavení
- `DiskDetectionService` - Služba pro detekci disků

### 4. UI Layer (DiskChecker.UI.Avalonia)

Prezentační vrstva s Avalonia UI.

**MVVM Pattern:**
- `ViewModelBase` - Základní třída pro view modely
- `INavigableViewModel` - Rozhraní pro navigovatelné view modely
- View Modely: `DiskSelectionViewModel`, `SurfaceTestViewModel`, `SmartCheckViewModel`, ...

**Navigace:**
- `INavigationService` - Služba pro navigaci mezi views
- `NavigationService` - Implementace navigace

**Dependency Injection:**
- Všechny služby registrovány v `App.axaml.cs`
- View modely vytvářeny přes DI kontejner

## Dependency Injection

```csharp
// App.axaml.cs - Registrace služeb
services.AddCoreServices();                    // Core služby
services.AddSingleton<DiskCheckerDbContext>(); // Databáze
services.AddSingleton<HistoryService>();       // Aplikační služby
services.AddSingleton<ISmartaProvider, WindowsSmartaProvider>(); // Infrastrukturní
services.AddTransient<DiskSelectionViewModel>(); // View modely
```

## Databáze

Aplikace používá **SQLite** přes Entity Framework Core.

**Tabulky:**
- `Drives` - Záznamy disků
- `Tests` - Záznamy testů
- `SmartaRecords` - SMART data
- `SurfaceTestSamples` - Vzorky rychlosti

## SMART Data Flow

```
┌──────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   smartctl   │────▶│ WindowsSmarta    │────▶│ SmartCheckService│
│  (process)   │     │   Provider       │     │                  │
└──────────────┘     └──────────────────┘     └─────────────────┘
                              │                        │
                              ▼                        ▼
                      ┌──────────────────┐     ┌─────────────────┐
                      │ SmartaData       │     │ QualityRating   │
                      │ (parsed JSON)    │     │ (calculated)    │
                      └──────────────────┘     └─────────────────┘
```

## Testing Flow

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│ User starts test│────▶│SurfaceTestService│────▶│ SurfaceTest     │
│                 │     │                  │     │ Executor        │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                                                       │
                                                       ▼
                                              ┌─────────────────┐
                                              │ Write/Verify    │
                                              │ sectors        │
                                              └─────────────────┘
                                                       │
                                                       ▼
                                              ┌─────────────────┐
                                              │ TestResult      │
                                              │ with errors     │
                                              └─────────────────┘
```

## Platform Support

| Platform | SMART Provider | Notes |
|----------|---------------|-------|
| Windows | WindowsSmartaProvider | Requires smartctl in PATH or smartmontools installed |
| Linux | LinuxSmartaProvider | Requires smartctl (apt install smartmontools) |
| macOS | LinuxSmartaProvider | Requires smartctl (brew install smartmontools) |

## Cross-Platform Considerations

- `Environment.OSVersion.Platform` pro detekci OS
- `OperatingSystem.IsWindows()`, `OperatingSystem.IsLinux()`, `OperatingSystem.IsMacOS()`
- Cesty k souborům přes `Path.Combine()`
- Oddělovače cest přes `Path.DirectorySeparatorChar`

## Settings Storage

Nastavení se ukládá do:
- **Windows:** `%LocalAppData%\DiskChecker\settings.json`
- **Linux:** `~/.config/DiskChecker/settings.json`
- **macOS:** `~/.config/DiskChecker/settings.json`

Databáze se ukládá do stejného adresáře jako `DiskChecker.db`.

## Backup System

Soubor zálohy obsahuje:
- `DiskChecker.db` - Databáze
- `DiskChecker.db-wal` - Write-ahead log (pokud existuje)
- `DiskChecker.db-shm` - Shared memory (pokud existuje)
- `settings.json` - Nastavení
- `backup_metadata.json` - Metadata zálohy