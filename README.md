# DiskChecker

**DiskChecker** je profesionální desktopová aplikace pro diagnostiku, zátěžové testování, sanitizaci a evidenci pevných disků a SSD. Projekt je postavený na .NET 10 a Avalonia UI, ukládá historii do SQLite a podporuje Windows i Linux.

Aplikace je určená pro servisní/testovací workflow: rychlá identifikace disku, SMART kontrola, povrchové a seek testy, destruktivní ověření disku, následné vytvoření karty disku, historie testů, porovnání disků a export certifikátu/reportu.

> [!WARNING]
> Některé režimy DiskCheckeru jsou **destruktivní** a mohou nenávratně smazat všechna data na vybraném disku. Před spuštěním sanitizace nebo destruktivního testu vždy ověřte vybraný disk, cestu zařízení a zálohy.

## Aktuální stav aplikace

DiskChecker je dnes více než jednoduchá SMART utilita. Obsahuje kompletní aplikační vrstvu, perzistenci, cross-platform desktop UI, platformně specifické přístupy k diskům a servisní nástroje okolo testů.

### Hlavní funkce

- **Detekce disků**
  - Windows: fyzické disky, svazky a doplňující informace přes systémová API/WMI.
  - Linux: detekce blokových zařízení a svazků, typicky nad `/dev/sdX`, `/dev/nvmeXnY`.
  - Rozpoznávání identity disku včetně modelu, sériového čísla, firmware, kapacity, typu sběrnice a připojení.
- **SMART/SMARTA diagnostika**
  - Čtení SMART dat přes platformní providery.
  - Pokročilé atributy, teploty, self-testy, log self-testů a maintenance akce.
  - Cache SMART dat s konfigurovatelným TTL v `appsettings.json`.
  - Instrukce a pokus o instalaci závislostí tam, kde jsou potřeba externí nástroje.
- **Hodnocení zdraví disku**
  - Výpočet skóre a známky A–F.
  - Vyhodnocení kritických SMART atributů, chyb, výkonu a průběhu testů.
  - Analytická vrstva pro detekci anomálií a servisní doporučení.
- **Test povrchu**
  - Profily pro HDD/SSD/NVMe a různé operační režimy.
  - Vzorkování rychlosti, teploty, chyb a průběhu.
  - Vizualizace průběhu a ukládání výsledků do historie.
- **Seek test**
  - Test latencí přeskoků s doporučeným nastavením podle stavu disku.
  - Statistiky průměr/min/max/medián/P95/P99 a chybovost.
  - Konzervativní režimy pro staré nebo rizikové disky.
- **Destruktivní test / sanitizace**
  - Zápis nul, čtení/ověření a sběr metrik.
  - Volitelné vytvoření oddílu a formátování po dokončení.
  - Windows a Linux implementace přes `IDiskSanitizationService`.
  - Recovery informace, detailní chyby a záznam výsledků do testovací session.
- **Bezpečný destruktivní workflow**
  - Samostatná UI část pro bezpečnější průchod destruktivním testem.
  - Důraz na potvrzení vybraného zařízení a minimalizaci omylů.
- **Karty disků**
  - Servisní karta disku podle sériového čísla.
  - Evidence testovacích session, SMART snapshotů, skóre, známky, poznámek, archivace a zámků.
  - Detail disku se souhrnem historie a přechody na certifikáty/porovnání/reporty.
- **Certifikáty a reporty**
  - Generování certifikátů o stavu disku.
  - PDF/exportní workflow a prohlížeč certifikátů.
  - Kompletní reporty, náhled a tisk/export podle implementovaných možností UI.
- **Historie a databáze**
  - SQLite databáze `DiskChecker.db`.
  - Starší tabulky testů i nové entity `DiskCards`, `TestSessions`, `DiskCertificates`, `DiskArchives`.
  - Kompatibilitní patcher schématu při startu aplikace.
- **Porovnání disků**
  - Porovnání vybraných diskových karet a jejich výkonových/zdravotních metrik.
- **Záloha a obnova**
  - UI pro zálohování a obnovu databáze/nastavení a navázaných dat.
  - Režimy a detaily používání jsou popsány v uživatelské příručce.
- **E-mailová oznámení**
  - Uložení SMTP nastavení.
  - Odesílání reportů a notifikací po dokončení testu.
- **Lokalizace**
  - Aktuálně jsou v repozitáři lokalizační soubory `cs.json` a `en.json`.

## Projekty v řešení

| Projekt | Role |
| --- | --- |
| `DiskChecker.Core` | Doménové modely, rozhraní a základní služby. Obsahuje např. `CoreDriveInfo`, `SmartaData`, `DiskCard`, `TestSession`, `DiskCertificate`, `SurfaceTestResult`, `SeekTestResult`, `IQualityCalculator`, `ISmartaProvider`, `IDiskSanitizationService`. |
| `DiskChecker.Infrastructure` | Platformní a technická implementace: SMART provideři pro Windows/Linux, detekce svazků, surface/seek executory, sanitizace, SQLite persistence, repozitáře, generátor certifikátů a porovnání disků. |
| `DiskChecker.Application` | Aplikační/use-case vrstva: SMART check, diskové karty a testovací session, historie, reporty, certifikace, e-mail, nastavení, notifikace, archivace a analýza testů. |
| `DiskChecker.UI.Avalonia` | Hlavní desktopové UI v Avalonia + MVVM. Obsahuje view modely, views, navigaci, dialogy, lokalizaci, zálohy/obnovu a registraci DI. |
| `DiskChecker.TUI` | Samostatný terminálový/experimentální projekt mimo hlavní `.slnx` workflow. |
| `tests/DiskChecker.Tests` | Unit testy pro analýzu, certifikáty, identitu disku, sanitizační progress, seek testy, nastavení, SMART parser/cache a další části. |

## Architektura

```text
┌─────────────────────────────────────────────────────────────┐
│ DiskChecker.UI.Avalonia                                     │
│ Avalonia 11 · MVVM · CommunityToolkit.Mvvm · Views/VM       │
│ Navigace · dialogy · lokalizace · grafy · uživatelské flow  │
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
│ Windows/Linux SMART · detekce disků · sanitizace · SQLite    │
│ EF Core repository · executory testů · certificate services  │
└───────────────────────────────┬─────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────┐
│ DiskChecker.Core                                             │
│ Modely · rozhraní · QualityCalculator · shared contracts     │
└─────────────────────────────────────────────────────────────┘
```

Podrobnější technický popis je v dokumentu [`docs/ARCHITECTURE_CS.md`](docs/ARCHITECTURE_CS.md).

## Technologie a hlavní závislosti

- **.NET 10.0** (`net10.0`)
- **Avalonia 11.3.12**
  - Projekt záměrně drží Avalonia 11.3.12 kvůli kompatibilitě s `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5`.
- **CommunityToolkit.Mvvm**
- **Entity Framework Core + SQLite**
- **SkiaSharp**, **LiveCharts**, **OxyPlot**
- **MailKit/MimeKit** pro SMTP
- **smartmontools/smartctl** hlavně na Linuxu a pro pokročilé SMART scénáře
- **xUnit v3**, **NSubstitute** pro testy

## Požadavky

### Vývoj

- .NET 10.0 SDK podle `global.json`
- Windows 10+ nebo moderní Linux distribuce
- Git a běžné build nástroje dané platformy

### Běh aplikace

- Administrátorská/root oprávnění pro přístup k fyzickým diskům.
- Linux: `smartmontools` (`smartctl`) a práva k blokovým zařízením.
- Windows: aplikace má manifest pro vyžádání administrátorských práv.

> [!NOTE]
> Bez zvýšených oprávnění může fungovat část UI, ale detekce disků, SMART data, seek/surface testy nebo sanitizace mohou selhat nebo být nekompletní.

## Rychlý start pro vývojáře

```bash
# Obnova balíčků
dotnet restore

# Build
dotnet build --configuration Release

# Spuštění hlavní desktopové aplikace
dotnet run --project DiskChecker.UI.Avalonia

# Testy
dotnet test
```

Na Windows lze stejné příkazy spustit v PowerShellu/CMD. Pro diskové operace spusťte terminál jako Administrátor.

## Publikování

Ruční publish hlavní aplikace:

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

Nebo použijte skripty v kořeni repozitáře:

```bash
./build.sh      # restore, build a publish pro win-x64/linux-x64/linux-arm64
./package.sh    # vytvoření linux tarball/DEB balíčků z publish výstupů
```

Linux instalační workflow je popsáno v [`docs/LINUX_INSTALL.md`](docs/LINUX_INSTALL.md).

## Uživatelská dokumentace

- [`docs/USER_GUIDE_CS.md`](docs/USER_GUIDE_CS.md) – uživatelská příručka v češtině.
- [`docs/LINUX_INSTALL.md`](docs/LINUX_INSTALL.md) – instalace a řešení problémů na Linuxu.
- [`docs/ARCHITECTURE_CS.md`](docs/ARCHITECTURE_CS.md) – technická architektura a mapa komponent.

## Databáze a data aplikace

Výchozí desktopová aplikace registruje SQLite databázi jako:

```text
DiskChecker.db
```

Soubor se vytváří v pracovním adresáři spuštěné aplikace. Databáze obsahuje jak legacy tabulky (`DriveRecords`, `TestRecords`, `SmartaRecords`, `SurfaceTestSamples`), tak nové servisní entity pro karty disků, session, certifikáty, archivaci, SMTP nastavení a replikační frontu.

Při startu aplikace se volá `EnsureCreated()` a následně `SchemaCompatibilityPatcher.Apply(...)`, aby se starší databáze přizpůsobily aktuálnímu schématu.

## Konfigurace

Hlavní desktopový projekt kopíruje `appsettings.json` do výstupu. Aktuálně je využívané zejména nastavení SMART cache:

```json
{
  "SmartaCacheOptions": {
    "TtlMinutes": 10
  }
}
```

Pokud konfigurace chybí nebo je neplatná, aplikace používá výchozí TTL 10 minut.

## Struktura repozitáře

```text
DiskChecker/
├── DiskChecker.Core/              # Doménové modely, rozhraní, QualityCalculator
├── DiskChecker.Infrastructure/    # Platformní implementace, SMART, testy, sanitizace, SQLite
├── DiskChecker.Application/       # Aplikační služby a obchodní logika
├── DiskChecker.UI.Avalonia/       # Hlavní desktopové UI
│   ├── ViewModels/                # MVVM view modely obrazovek
│   ├── Views/                     # Avalonia AXAML views
│   ├── Services/                  # Navigace, dialogy, zálohy, lokalizace, stav dokumentů
│   ├── Converters/                # UI konvertory
│   ├── Locales/                   # cs/en překlady
│   └── Assets/                    # Ikony a assety
├── DiskChecker.TUI/               # Terminálový/experimentální projekt
├── tests/DiskChecker.Tests/       # Unit testy
├── docs/                          # Uživatelská a technická dokumentace
├── installer/                     # Linux/Windows instalační podklady
├── scripts/                       # Pomocné build skripty
├── build.sh                       # Cross-runtime publish script
├── package.sh                     # Linux packaging script
└── version.properties             # Verze pro packaging
```

## Bezpečnostní poznámky

- Destruktivní testy a sanitizace pracují s fyzickým zařízením, nikoli jen se souborem.
- Na Linuxu může být zařízení označeno např. `/dev/sda`; na Windows např. `\\.\PhysicalDrive1`.
- Nikdy netestujte systémový disk destruktivním režimem.
- U externích USB adaptérů může být SMART částečně nedostupný nebo zkreslený.
- Během dlouhých testů neodpojujte disk a nepřerušujte napájení.

## Testování

```bash
dotnet test
```

Testovací projekt pokrývá zejména výpočty/analýzy, generování certifikátů, identitu disků, sanitizační progress, seek testy, nastavení a SMART parsery/cache.

## Licence

Viz soubor [`LICENSE`](LICENSE).
