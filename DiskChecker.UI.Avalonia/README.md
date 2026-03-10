# DiskChecker UI.Avalonia

Avalonia UI aplikace pro kontrolu zdraví disků pomocí SMART dat a testování povrchu.

## Funkce

- **Výběr disků** - Zobrazení seznamu dostupných disků s jejich SMART daty a hodnocením zdraví
- **SMART kontrola** - Detailní zobrazení SMART atributů a jejich hodnocení
- **Test povrchu** - Testování povrchu disku s vizualizací rychlosti a průběhu
- **Analýza** - Pokročilá analýza zdraví disku
- **Historie** - Historie provedených testů
- **Reporty** - Generování reportů o zdraví disku
- **Nastavení** - Konfigurace aplikace a zálohování

## Struktura projektu

```
DiskChecker.UI.Avalonia/
├── App.axaml(.cs)          # Hlavní aplikace a DI konfigurace
├── Program.cs              # Vstupní bod aplikace
├── ViewLocator.cs          # Locator pro view modely
├── ViewModels/              # View modely (MVVM)
│   ├── ViewModelBase.cs    # Základní třída pro view modely
│   ├── INavigableViewModel.cs
│   ├── MainWindowViewModel.cs
│   ├── DiskSelectionViewModel.cs
│   ├── SurfaceTestViewModel.cs
│   ├── SmartCheckViewModel.cs
│   ├── AnalysisViewModel.cs
│   ├── HistoryViewModel.cs
│   ├── ReportViewModel.cs
│   ├── SettingsViewModel.cs
│   ├── DiskStatusCardItem.cs
│   ├── DataPoint.cs
│   ├── TestHistoryItem.cs
│   └── TestProfileItem.cs
├── Views/                   # Views (AXAML)
│   ├── MainWindow.axaml(.cs)
│   ├── DiskSelectionView.axaml(.cs)
│   ├── SurfaceTestView.axaml(.cs)
│   ├── SmartCheckView.axaml(.cs)
│   ├── AnalysisView.axaml(.cs)
│   ├── HistoryView.axaml(.cs)
│   ├── ReportView.axaml(.cs)
│   └── SettingsView.axaml(.cs)
├── Services/                # Služby
│   ├── NavigationService.cs
│   ├── DialogService.cs
│   ├── BackupService.cs
│   └── Interfaces/
│       ├── INavigationService.cs
│       ├── IDialogService.cs
│       ├── IBackupService.cs
│       ├── IHistoryService.cs
│       ├── IAnalysisService.cs
│       └── IDiskSelectionService.cs
├── Converters/               # Konvertory pro binding
│   ├── BooleanToBrushConverter.cs
│   ├── DiskColorConverters.cs
│   ├── EnumToBooleanConverter.cs
│   ├── InverseBooleanConverter.cs
│   └── NotNullConverter.cs
└── Styles/                  # Styly
    └── CommonStyles.axaml
```

## Architektura

Aplikace používá **MVVM (Model-View-ViewModel)** pattern s následujícími technologiemi:

- **Avalonia UI** - Cross-platform UI framework
- **CommunityToolkit.Mvvm** - Source generators pro MVVM
- **Microsoft.Extensions.DependencyInjection** - Dependency injection

### Dependency Injection

Všechny služby jsou registrovány v `App.axaml.cs`:

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // Core services
    services.AddCoreServices();

    // Database
    services.AddSingleton<DiskCheckerDbContext>();

    // Application services
    services.AddSingleton<HistoryService>();
    services.AddSingleton<SettingsService>();
    // ...

    // View models
    services.AddTransient<DiskSelectionViewModel>();
    // ...
}
```

## Spuštění

```bash
cd D:\DiskChecker
dotnet run --project DiskChecker.UI.Avalonia
```

## Závislosti

- DiskChecker.Core - Modely a rozhraní
- DiskChecker.Infrastructure - Implementace služeb
- DiskChecker.Application - Aplikační služby

## Lokalizace

Aplikace je lokalizována do češtiny. Texty jsou přímo ve zdrojovém kódu.