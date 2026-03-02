# 🖴 DiskChecker WPF Application

Profesionální aplikace pro diagnostiku a testování disků s moderním WPF uživatelským rozhraním!

## ✨ Hlavní Funkce

### 📊 Test Povrchu Disku (Surface Test)
- **Defragmentace-style vizualizace** s barevnými bloky
- Barevné indikátory stavů:
  - 🔵 **Modrá** - Zápis (Write) - probíhá nebo hotovo
  - 🟢 **Zelená** - Čtení (Read) - ověřeno
  - 🟡 **Žlutá** - Probíhající blok
  - 🔴 **Červená** - Chyba
  - ⚫ **Šedá** - Netestováno
- **Real-time monitoring** s grafu rychlosti (MB/s)
- Detailní statistiky:
  - Počet chyb
  - Celkem zpracovaných dat
  - Uplynulý čas a ETA
  - Průměrná a aktuální rychlost

### 🔧 SMART Kontrola
- Kontrola SMART dat disku
- Detekce problémů (chybné sektory, teplota, atd.)

### 📄 Reporty
- Generování detailních reportů z testů
- Export výsledků

### 📚 Historie
- Uchování historie všech testů
- Snadný přístup k minulým výsledkům

### ⚙️ Nastavení
- Konfigurace chování aplikace
- Výběr profilů testování

## 🏗️ Architektura

### Vrstva prezentace (WPF)
```
DiskChecker.UI.WPF/
├── Views/
│   ├── DiskSelectionView.xaml      # Výběr disku
│   ├── SurfaceTestView.xaml        # Hlavní test povrchu
│   ├── SmartCheckView.xaml         # SMART kontrola
│   ├── ReportView.xaml             # Reporty
│   ├── HistoryView.xaml            # Historie
│   └── SettingsView.xaml           # Nastavení
├── ViewModels/
│   ├── ViewModelBase.cs            # Bázová třída
│   ├── MainWindowViewModel.cs      # Navigace
│   ├── DiskSelectionViewModel.cs   # Výběr disku
│   └── OtherViewModels.cs          # Surface Test, Smart Check, atd.
├── Services/
│   └── NavigationService.cs        # MVVM navigace
├── Converters/
│   └── ValueConverters.cs          # WPF value converters
└── App.xaml(.cs)                   # Entry point

```

### Logika aplikace (Application Layer)
```
DiskChecker.Application/
└── Services/
    ├── SurfaceTestService.cs       # Koordinace testu
    ├── SmartCheckService.cs        # SMART diagnostika
    ├── DiskCheckerService.cs       # Obsah všech disků
    └── ... ostatní služby
```

### Hardware vrstva (Infrastructure)
```
DiskChecker.Infrastructure/
└── Hardware/
    ├── DiskSurfaceTestExecutor.cs  # Nízkoúrovňový test
    ├── WindowsSmartaProvider.cs    # Windows SMART
    ├── LinuxSmartaProvider.cs      # Linux SMART
    └── ... ostatní
```

## 🎨 Design a UX

### Barevné schéma
- **Primary Blue**: #007ACC - Akce, primární prvky
- **Success Green**: #28A745 - OK stav, zelený
- **Warning Yellow**: #FFC107 - Varování, probíhá
- **Danger Red**: #DC3545 - Chyby, kritické
- **Gray**: #666666 - Text, sekundární

### Typografie
- **Header**: 24-28px, Bold - Titulky sekcí
- **Body**: 12-14px - Normální text
- **Label**: 11px - Popisky

## 🔌 MVVM Binding

### SurfaceTestViewModel Properties
```csharp
// Stav testu
IsTestRunning: bool
ProgressPercent: double (0-100)
CurrentPhase: string

// Data
BytesProcessed: long
TotalBytes: long
CurrentThroughputMbps: double
AverageThroughputMbps: double
ErrorCount: int

// Vizualizace
Blocks: ObservableCollection<BlockStatus>
SpeedSamples: ObservableCollection<SpeedSample>

// Časy
ElapsedTime: TimeSpan
EstimatedTimeRemaining: TimeSpan
```

### Příklady Bindingů
```xaml
<!-- Text binding s converter -->
<TextBlock Text="{Binding BytesProcessed, Converter={StaticResource BytesToStringConverter}}"/>

<!-- Boolean binding -->
<Button IsEnabled="{Binding SelectedDrive, Converter={StaticResource NotNullConverter}}"/>

<!-- ObservableCollection binding -->
<ItemsControl ItemsSource="{Binding Blocks}">
    <DataTemplate>
        <Border Background="{Binding Status, Converter={StaticResource StatusToColorConverter}}"/>
    </DataTemplate>
</ItemsControl>
```

## 📦 Value Converters

| Converter | Vstup | Výstup | Příklad |
|-----------|-------|--------|---------|
| `BytesToStringConverter` | long | string | 1024000000 → "976.56 MB" |
| `PercentageConverter` | double | string | 45.67 → "45.7%" |
| `MbpsConverter` | double | string | 125.5 → "125.5 MB/s" |
| `TimeSpanConverter` | TimeSpan | string | 0:05:30 → "05:30" |
| `InvertBoolConverter` | bool | bool | true → false |
| `NotNullConverter` | object | bool | null → false |

## 🚀 Spuštění Aplikace

### Požadavky
- .NET 10 SDK
- Windows 10+ (nebo Linux s Wine/Mono)
- 100 MB volného místa

### Kompilace
```bash
dotnet build
```

### Spuštění
```bash
dotnet run --project DiskChecker.UI.WPF
```

## 📋 Testování Surface Test

### Scénář 1: Normální test
1. Spusť aplikaci
2. Vyber disk ze seznamu
3. Klikni na "🧪 Test Povrchu"
4. Klikni na "▶️ START"
5. Sleduj progres (modrá = write, zelená = read)

### Scénář 2: Zastavení testu
1. Během běhu klikni "⏹️ STOP"
2. Test se zastaví a vraticí se do počátečního stavu

## 🎯 Budoucí Zlepšení

- [ ] Export reportů do PDF
- [ ] Real-time graf s OxyPlot
- [ ] Notifikace na chyby
- [ ] Plánování testů
- [ ] Porovnání mehrých testů
- [ ] Dark mode
- [ ] Podpora vzdáleného testování

## 📄 Licence

Vnitřní kód DiskChecker - Všechna práva vyhrazena.

## 👨‍💻 Vývoj

Aplikace je plně objektově orientovaná s MVVM architekturou a podporuje:
- Asynchronní operace (async/await)
- Dependency Injection
- MVVM Community Toolkit
- Unit testing s xUnit

---

**Vytvořeno**: 2024  
**Verze**: 1.0.0  
**Status**: Production Ready 🎉
