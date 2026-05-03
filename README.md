# DiskChecker

Profesionální nástroj pro diagnostiku a testování disků pomocí SMART dat a povrchových testů.

## Projekty

| Projekt | Popis |
|---------|-------|
| **DiskChecker.Core** | Základní modely, rozhraní a kalkulátor kvality |
| **DiskChecker.Infrastructure** | Implementace služeb (SMART, databáze, sanitzace) |
| **DiskChecker.Application** | Aplikační služby a obchodní logika |
| **DiskChecker.UI.Avalonia** | Cross-platform desktop UI (Avalonia + MVVM) |

## Architektura

```
┌─────────────────────────────────────────────────────────┐
│                  UI.Avalonia                             │
│  (Avalonia UI · MVVM · CommunityToolkit.Mvvm)          │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                  Application                             │
│  (SmartCheckService · HistoryService · ReportService)   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                  Infrastructure                          │
│  (WindowsSmartaProvider · LinuxSmartaProvider · SQLite) │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                     Core                                 │
│  (CoreDriveInfo · SmartaData · QualityRating)           │
│  (ISmartaProvider · IQualityCalculator)                 │
└─────────────────────────────────────────────────────────┘
```

## Funkce

- **SMART kontrola** – čtení a analýza SMART atributů z disků
- **Hodnocení zdraví** – výpočet kvality disku (stupeň A–F)
- **Test povrchu** – zápis a ověření dat na disku s vizualizací průběhu
- **Historie testů** – ukládání výsledků do SQLite databáze
- **Certifikáty** – generování PDF certifikátů o stavu disku
- **Reporty** – přehledy a analýzy zdraví disku
- **Sanitzace** – bezpečné mazání disků (DoD 5220.22-M, Gutmann, NVMe)
- **Zálohování** – záloha a obnova databáze a nastavení

## Požadavky

- .NET 10.0 SDK
- Windows 10+ / Linux (x64, ARM64)
- `smartctl` z balíčku `smartmontools` (vyžadováno na Linuxu)
- Root/admin práva pro přístup k diskům

## Sestavení

```bash
# Restore balíčků
dotnet restore

# Build řešení
dotnet build --configuration Release

# Spuštění
dotnet run --project DiskChecker.UI.Avalonia
```

## Publikování

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

## Struktura projektu

```
DiskChecker/
├── DiskChecker.Core/              # Modely a rozhraní
│   ├── Models/                    # Datové modely (CoreDriveInfo, SmartaData, …)
│   ├── Interfaces/                # Rozhraní služeb
│   ├── Extensions/                 # Pomocné rozšíření
│   └── Services/                  # Základní služby (QualityCalculator)
├── DiskChecker.Infrastructure/    # Implementace
│   ├── Hardware/                  # SMART provideři, testování, sanitzace
│   ├── Persistence/               # SQLite DbContext, repozitáře
│   ├── Configuration/             # Konfigurace
│   ├── Helpers/                   # Pomocné třídy
│   └── Services/                  # CertificateGenerator, ComparisonService
├── DiskChecker.Application/       # Aplikační logika
│   ├── Services/                  # Služby (SmartCheck, History, Report, …)
│   ├── Constants/                 # Konstanty aplikace
│   └── Extensions/                # Pomocná rozšíření
├── DiskChecker.UI.Avalonia/       # Desktop UI
│   ├── ViewModels/                # MVVM view modely
│   ├── Views/                     # AXAML views
│   ├── Services/                  # UI služby, navigace, dialogy
│   ├── Converters/                # Hodnotové konvertory
│   └── Styles/                    # Styly
├── tests/                         # Testy
│   └── DiskChecker.Tests/         # Unit testy
├── installer/                      # Instalační skripty a konfigurace
├── scripts/                       # Build skripty
└── docs/                          # Dokumentace
```

## Licence

Viz soubor [LICENSE](LICENSE).