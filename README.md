# DiskChecker

Aplikace pro kontrolu zdraví disků pomocí SMART dat a testování povrchu.

## Projekty

Solution obsahuje následující projekty:

| Projekt | Popis |
|---------|-------|
| **DiskChecker.Core** | Základní modely a rozhraní |
| **DiskChecker.Infrastructure** | Implementace služeb (SMART, databáze) |
| **DiskChecker.Application** | Aplikační služby a logika |
| **DiskChecker.UI.Avalonia** | Hlavní UI aplikace (Avalonia) |

## Architektura

```
┌─────────────────────────────────────────────────────────┐
│                   UI.Avalonia                            │
│  (Avalonia UI + MVVM + CommunityToolkit.Mvvm)           │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   Application                            │
│  (Služby aplikace: HistoryService, SmartCheckService)   │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                   Infrastructure                         │
│  (Implementace: WindowsSmartaProvider, DbContext)       │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│                     Core                                 │
│  (Modely: SmartaData, QualityRating, CoreDriveInfo)    │
│  (Rozhraní: ISmartaProvider, IQualityCalculator)       │
└─────────────────────────────────────────────────────────┘
```

## Funkce

- **SMART kontrola** - Čtení SMART dat z disků
- **Hodnocení zdraví** - Výpočet kvality disku (A-F)
- **Test povrchu** - Zápis a ověření dat na disku
- **Historie testů** - Ukládání výsledků do databáze
- **Reporty** - Generování PDF reportů
- **Zálohování** - Záloha a obnova databáze

## Požadavky

- .NET 10.0
- Windows / Linux / macOS
- smartctl (pro SMART data na Linuxu)

## Sestavení

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run Avalonia app
dotnet run --project DiskChecker.UI.Avalonia
```

## Struktura řešení

```
DiskChecker/
├── DiskChecker.Core/           # Modely a rozhraní
│   ├── Models/                 # Datové modely
│   ├── Interfaces/             # Rozhraní služeb
│   └── Services/               # Základní služby
├── DiskChecker.Infrastructure/ # Implementace
│   ├── Hardware/               # SMART, testování
│   ├── Persistence/            # Databáze
│   └── Helpers/                # Pomocné třídy
├── DiskChecker.Application/    # Aplikační logika
│   └── Services/               # Aplikační služby
├── DiskChecker.UI.Avalonia/    # UI aplikace
│   ├── ViewModels/             # MVVM view modely
│   ├── Views/                  # AXAML views
│   ├── Services/               # UI služby
│   └── Converters/             # Konvertory
└── _Archived/                  # Archivované projekty
    ├── DiskChecker.UI.WPF/     # WPF UI (odpojeno)
    ├── DiskChecker.Tests/      # Testy (odpojeno)
    └── ...
```

## License

Viz soubor [LICENSE](LICENSE).

## Autor

DiskChecker Team