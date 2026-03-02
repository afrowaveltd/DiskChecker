# DiskChecker WPF Desktop Application - Migrační plán

## 📋 Soubor: WPF_MIGRATION_PLAN.md

## 1. Analýza stávající architektury

### Aktuální struktura
```
DiskChecker.Core          - Modely (SurfaceTestResult, SmartaData, atd.)
DiskChecker.Application   - Services (DiskChecker, SmartCheck, TestReport, History)
DiskChecker.Infrastructure - Executory (RawDisk, DiskSurfaceTest)
DiskChecker.UI (Console)  - Terminálová aplikace (Spectre.Console)
```

### Funkce k migraci
1. ✅ SMART check (čtení informací o disku)
2. ✅ Surface test (zápis/ověření)
3. ✅ Full disk sanitization (přepis nulami)
4. ✅ Report a analýza
5. ✅ Historie testů
6. ✅ Porovnání disků
7. ✅ Formátování (GPT + NTFS)
8. ✅ Export (JSON, CSV, PDF, Email)

## 2. WPF Architektura (MVVM)

### Projektová struktura
```
DiskChecker.UI.WPF/
├── App.xaml                          # Entry point
├── MainWindow.xaml                   # Hlavní okno
├── Resources/
│   ├── Icons/                        # Ikony (SVG → PNG konverze)
│   ├── Styles.xaml                   # Globální styly
│   └── Colors.xaml                   # Barevné schéma
├── Views/
│   ├── DiskSelection/
│   │   ├── DiskSelectionView.xaml
│   │   └── DiskSelectionViewModel.cs
│   ├── SmartCheck/
│   │   ├── SmartCheckView.xaml
│   │   └── SmartCheckViewModel.cs
│   ├── SurfaceTest/
│   │   ├── SurfaceTestView.xaml      # Hlavní progress s reálnými grafy
│   │   └── SurfaceTestViewModel.cs
│   ├── Report/
│   │   ├── ReportView.xaml
│   │   └── ReportViewModel.cs
│   ├── History/
│   │   ├── HistoryView.xaml
│   │   └── HistoryViewModel.cs
│   ├── Settings/
│   │   ├── SettingsView.xaml
│   │   └── SettingsViewModel.cs
│   └── Shared/
│       ├── LoadingView.xaml
│       └── ProgressView.xaml
├── Services/
│   ├── NavigationService.cs          # Navigace mezi Views
│   ├── DialogService.cs              # Dialogy (potvrz., otevření)
│   └── ThemeService.cs               # Tmavý/světlý motiv
├── Behaviors/                         # Attached behaviors
│   ├── FocusBehavior.cs
│   └── DoubleClickBehavior.cs
└── Converters/                        # Value converters
    ├── BytesToHumanReadableConverter.cs
    ├── PercentageToColorConverter.cs
    └── TemperatureToColorConverter.cs
```

## 3. Klíčové WPF komponenty

### Progress and Monitoring (Surface Test)
- **ProgressBar**: Globální 0-100% (zápis + ověření)
- **Phase Indicator**: Zápis (0-50%) / Ověření (50-100%)
- **Real-time Speed Graph**: LineChart (OxyPlot/LiveCharts2)
- **Temperature Gauge**: Kruhový metr (0-100°C)
- **Error Counter**: Dynamické čítače s barvou
- **Time Remaining**: Počítaný ETA v reálném čase
- **Current Speed**: Aktuální MB/s s průměrem

### Report Visualization
- **Overall Grade Badge**: Velké A-F s barvou
- **Performance Charts**: 
  - Rychlost v čase (čára)
  - Teplota v čase (čára)
  - Chyby v čase (sloupcový graf)
- **Statistics Table**: Shrnutí výsledků
- **Health Indicators**: Zelená/žlutá/červená

### Notifications & Feedback
- **Toast Notifications**: V rohu okna
- **Progress Dialog**: Modální okno během dlouhých operací
- **Status Bar**: Dolů - aktuální stav

## 4. Technologické volby

### UI Framework
- **WPF** (C#14, .NET 10)
- **MVVM Toolkit** (Microsoft.Mvvm.Toolkit) - pro RelayCommand, ObservableProperty

### Grafování
- **OxyPlot** nebo **LiveCharts2** - pro reálné grafy
- **MahApps.Metro** - pro moderní komponenty (Toggle, Metro buttons)
- **Hardcodet.NotifyIcon.Wpf** - pro system tray ikonu

### Kontroly
- **Syncfusion** nebo open-source alternativy:
  - DataGrid pro tabulky
  - Circular Progress pro teplotu
  - Donut chart pro statistiky

### Databáze
- Zachovat existující **SQLite** (DiskChecker.Application)

## 5. Stav navigace (State Machine)

```
START
  ↓
DiskSelection (Výběr disku)
  ├→ SmartCheck (Čtení SMART)
  ├→ SurfaceTest (Zápis/Ověření)
  │   └→ Report (Zobrazení výsledků)
  ├→ History (Přehled testů)
  ├→ Settings (Nastavení)
  └→ EXIT
```

## 6. Binding a komunikace

### MVVM Pattern
- **View** → XAML (UI definice)
- **ViewModel** → C# (logika, bindování, commands)
- **Model** → Existující `DiskChecker.Core.Models`
- **Services** → Existující `DiskChecker.Application.Services`

### Data Flow
```
User Action (View)
  ↓
RelayCommand (ViewModel)
  ↓
Service Call (DiskChecker.Application)
  ↓
Progress/Result reporting
  ↓
ObservableCollection/Property update
  ↓
UI refresh (WPF binding)
```

## 7. Progress Reporting - Interaktivní

### During Surface Test
```
Real-time updates každých 500ms:
- Current speed: {MB/s}
- Average speed: {MB/s}
- Phase: Write / Verify
- Progress: {X}%
- ETA: {HH:mm:ss}
- Temperature: {°C}
- Errors: {count}
- Time elapsed: {HH:mm:ss}
```

### Graficky
```
[████████░░░░░░░░░░░░░░░░░] 35%
├─ Fáze 1: Zápis [████████░░░░░░] 50% (Complete)
├─ Fáze 2: Ověření [████░░░░░░░░░░] 20% (In Progress)
│
├─ Rychlost: 150.5 MB/s
├─ Průměr: 145.2 MB/s
├─ Teplota: 42°C (zelená)
├─ Chyby: 0 ✅
└─ Zbývá: 15m 23s
```

## 8. Theme/Styling

### Barevné schéma
- **Primary**: #007ACC (Visual Studio modrá)
- **Success**: #27AE60 (zelená)
- **Warning**: #F39C12 (oranžová)
- **Error**: #E74C3C (červená)
- **Background**: #F5F5F5 (světlé) / #2D2D2D (tmavé)

### Icons
- Převzít/konvertovat z `Resources/icon.svg`
- Minimalistické ikony (Material Design)
- 16x16, 32x32, 64x64 pro různé použití

## 9. Fazovitá implementace

### Fáze 1: Setup (1 den)
- [ ] Vytvořit WPF projekt
- [ ] Nastavit MVVM Toolkit
- [ ] Konfigurovat DI container
- [ ] Vytvořit MainWindow + Navigation

### Fáze 2: Základní funkce (2-3 dny)
- [ ] DiskSelectionView
- [ ] SmartCheckView (čtení + zobrazení)
- [ ] Formátování disku View

### Fáze 3: Surface Test + Real-time (3-4 dny)
- [ ] SurfaceTestViewModel s progress binding
- [ ] Real-time speed graph
- [ ] Temperature gauge
- [ ] Error counter
- [ ] ETA calculator

### Fáze 4: Report (1-2 dny)
- [ ] ReportView s statistikami
- [ ] Performance charty
- [ ] Export funkcionalita

### Fáze 5: Historie + Porovnání (1-2 dny)
- [ ] HistoryView
- [ ] ComparisonView
- [ ] Database queries

### Fáze 6: Dotish (1 den)
- [ ] SettingsView
- [ ] Theme switching
- [ ] Error handling
- [ ] Notifications

## 10. Migrace kódu

### Core layery (žádné změny)
```
DiskChecker.Core/         ← Beze změn
DiskChecker.Application/  ← Beze změn
DiskChecker.Infrastructure/ ← Beze změn
```

### UI vrstva
```
DiskChecker.UI.Console/   ← Zachovat
DiskChecker.UI.WPF/       ← Nový projekt
```

**Společná logika**: Všechny Services z Application vrstvy se budou moci používat v obou UI projektech!

## 11. Budoucí rozšíření

- [ ] Linux GTK verze
- [ ] Web API + Web UI
- [ ] Mobile companion app
- [ ] Real-time telemetry cloud upload
- [ ] Predictive analytics (ML model zdraví disku)

---

**Status**: Ready for Phase 1 implementation
**Estimated Duration**: 2-3 týdny pro plné funkční WPF verzi
**Team**: 1 developer (solo development)
