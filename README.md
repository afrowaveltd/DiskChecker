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
  - Čtení SMART dat přes platformní providery (Windows i Linux).
  - Pokročilé atributy, teploty, self-testy, log self-testů a maintenance akce.
  - Cache SMART dat s konfigurovatelným TTL v `appsettings.json`.
  - **Negativní cache** – pokud disk SMART nepodporuje (USB adaptéry, RAID řadiče), uloží se sentinel do cache na 30 minut, aby se předešlo opakovaným timeoutům a zamrzání UI.
  - Instrukce a pokus o instalaci závislostí tam, kde jsou potřeba externí nástroje.
- **SMART historické trendy**
  - Automatické ukládání SMART snapshotů do dedikované tabulky `SmartSnapshots` při každém testu.
  - **Trendová analýza** napříč testy – teplota, reallocated sectors, wear leveling, percentage used, pending sectors a další.
  - Lineární regrese pro výpočet rychlosti degradace a predikce dnů do kritické meze.
  - Vizuální zobrazení trendových grafů přímo v analytickém pracovišti.
- **Vendor-specific hodnocení opotřebení SSD**
  - Mapování Wear_Leveling_Count (ID 177) podle výrobce – Samsung, Intel, Seagate, Western Digital, SanDisk, Crucial/Micron, Kingston, Toshiba/Kioxia, SK Hynix, ADATA, Corsair a další.
  - Interpretace normalizované hodnoty jako zbývající životnosti v % (100 = nový, 0 = mrtvý).
  - Automatické rozpoznání NVMe zařízení a použití standardizovaného `PercentageUsed`.
  - Lidsky čitelný popis stavu opotřebení s barevnou severitou.
- **Hodnocení zdraví disku**
  - Výpočet skóre a známky A–F.
  - Vyhodnocení kritických SMART atributů, chyb, výkonu a průběhu testů.
  - **Adaptivní detekce anomálií** – dvouúrovňové vzorkování rychlosti s detekcí výkonových propadů, jejich high-res záznamem a analýzou.
  - **AnomalyAnalysisService** – párování write+read anomálií, výpočet korelace, detekce opakujících se vzorů, penalizace do skóre.
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
  - **Adaptivní vzorkování** během sanitizace – standardní vzorky pro graf + high-res záznam anomálií pro pozdější analýzu.
- **Absolutní destruktivní test**
  - Kompletní workflow: 2× sanitizace (write+read), 3× seek test (full-stroke, random, skip), SMART před/po.
  - Automatické generování certifikátu s grafy, metrikami a analýzou anomálií.
  - Detailní report s překryvným porovnáním write/read anomálií.
- **Bezpečný destruktivní workflow**
  - Samostatná UI část pro bezpečnější průchod destruktivním testem.
  - Záloha před testem, destruktivní test, obnova po testu.
  - Důraz na potvrzení vybraného zařízení a minimalizaci omylů.
- **Analytické pracoviště**
  - Prohlížeč historických testovacích session s detailními grafy.
  - **Throughput grafy** – write/read rychlost podle průběhu disku i podle času.
  - **Seek latency grafy** – latence přeskoků podle indexu.
  - **Teplotní trendy** – vývoj teploty během testu.
  - **Detekce anomálií a stallů** – přetížené oblasti v grafu s možností zoomu.
  - **SMART trendy** – vývoj klíčových metrik napříč testy (teplota, reallocated, wear leveling, % used).
  - **Vendor-specific hodnocení opotřebení** – interpretace Wear_Leveling_Count podle výrobce.
  - Kompaktní a plný režim zobrazení, automatické přizpůsobení šířce okna.
- **Karty disků**
  - Servisní karta disku podle sériového čísla.
  - Evidence testovacích session, SMART snapshotů, skóre, známky, poznámek, archivace a zámků.
  - Detail disku se souhrnem historie a přechody na certifikáty/porovnání/reporty.
- **Certifikáty a reporty**
  - Generování certifikátů o stavu disku s grafy, metrikami a analýzou anomálií.
  - **Cross-platform** – PDF i JPEG náhled přes SkiaSharp (Windows, Linux, macOS).
  - Generování štítků (PNG).
  - PDF/exportní workflow a prohlížeč certifikátů.
  - Kompletní reporty, náhled a tisk/export podle implementovaných možností UI.
- **Historie a databáze**
  - SQLite databáze `DiskChecker.db`.
  - Starší tabulky testů i nové entity `DiskCards`, `TestSessions`, `DiskCertificates`, `DiskArchives`, `SmartSnapshots`.
  - Kompatibilitní patcher schématu při startu aplikace (automatické přidávání chybějících sloupců).
- **Porovnání disků**
  - Porovnání vybraných diskových karet a jejich výkonových/zdravotních metrik.
- **Záloha a obnova**
  - **Tři režimy zálohy**: souborová, RAW obraz (sektory), VHDx dynamický obraz.
  - **VHDx** – standardní Microsoft formát, mountovatelný ve Windows nativně i v Linuxu přes `qemu-nbd`.
  - **Odolnost vůči chybám** – nečitelné sektory jsou nahrazeny nulami, záloha pokračuje; ochrana proti katastrofickému selhání (limit po sobě jdoucích chyb).
  - **Větší bloky** (1 MiB) pro rychlejší přenos.
  - UI pro výběr cílového umístění s přehledem volného místa.
  - Obnova ze zálohy včetně verifikace.
- **E-mailová oznámení**
  - Uložení SMTP nastavení.
  - Odesílání reportů a notifikací po dokončení testu.
- **Lokalizace**
  - Aktuálně jsou v repozitáři lokalizační soubory `cs.json` a `en.json`.
  - Dynamické přepínání jazyka za běhu aplikace.

## Projekty v řešení

| Projekt | Role |
| --- | --- |
| `DiskChecker.Core` | Doménové modely, rozhraní a základní služby. Obsahuje např. `CoreDriveInfo`, `SmartaData`, `DiskCard`, `TestSession`, `DiskCertificate`, `SpeedAnomaly`, `SurfaceTestResult`, `SeekTestResult`, `AdaptiveSpeedSampler`, `AnomalyAnalysisService`, `QualityCalculator`, `SmartTrendService`, `VendorWearMapping`. |
| `DiskChecker.Infrastructure` | Platformní a technická implementace: SMART provideři pro Windows/Linux, detekce svazků, surface/seek executory, sanitizace, SQLite persistence, repozitáře, generátor certifikátů (SkiaSharp), porovnání disků a SchemaCompatibilityPatcher. |
| `DiskChecker.Application` | Aplikační/use-case vrstva: SMART check, diskové karty a testovací session, historie, reporty, certifikace, e-mail, nastavení, notifikace, archivace a analýza testů. |
| `DiskChecker.UI.Avalonia` | Hlavní desktopové UI v Avalonia + MVVM. Obsahuje view modely, views, navigaci, dialogy, lokalizaci, zálohy/obnovu a registraci DI. |
| `DiskChecker.TUI` | Samostatný terminálový/experimentální projekt mimo hlavní `.slnx` workflow. |
| `tests/DiskChecker.Tests` | Unit testy (190 testů) pro adaptivní vzorkování, analýzu anomálií, certifikáty, identitu disku, sanitizační progress, seek testy, nastavení, SMART parser/cache a další části. |

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
│ Modely · rozhraní · AdaptiveSpeedSampler ·                   │
│ AnomalyAnalysisService · QualityCalculator · SmartTrendService│
│ VendorWearMapping · shared contracts                         │
└─────────────────────────────────────────────────────────────┘
```

Podrobnější technický popis je v dokumentu [`docs/ARCHITECTURE_CS.md`](docs/ARCHITECTURE_CS.md).

## Technologie a hlavní závislosti

- **.NET 10.0** (`net10.0`)
- **Avalonia 11.3.12**
  - Projekt záměrně drží Avalonia 11.3.12 kvůli kompatibilitě s `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5`.
- **CommunityToolkit.Mvvm 8.4.2**
- **Entity Framework Core + SQLite**
- **SkiaSharp 3.119.4** – cross-platform rendering pro certifikáty, grafy a štítky
- **LiveChartsCore 2.0.5** – grafy v UI (povrchový test, seek test, sanitizace)
- **OxyPlot.Avalonia 2.1.0** – doplňkové grafy
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

Soubor se vytváří v pracovním adresáři spuštěné aplikace. Databáze obsahuje jak legacy tabulky (`DriveRecords`, `TestRecords`, `SmartaRecords`, `SurfaceTestSamples`), tak nové servisní entity pro karty disků, session, certifikáty, archivaci, SMART snapshoty, SMTP nastavení a replikační frontu.

Při startu aplikace se volá `EnsureCreated()` a následně `SchemaCompatibilityPatcher.Apply(...)`, aby se starší databáze přizpůsobily aktuálnímu schématu. Patcher automaticky přidává chybějící sloupce (např. `AnomaliesJson`, `Sanitize1ResultJson`, `SeekResultsJson`) bez ztráty existujících dat.

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

## Klíčové funkce – detail

### Adaptivní vzorkování a detekce anomálií

Během sanitizačních testů DiskChecker používá dvouúrovňový `AdaptiveSpeedSampler`:

- **Standardní vzorky** (~200 bodů) – pro graf a uložení do databáze, s časovou decimací.
- **High-res anomálie** (100ms intervaly) – spuštěny při odchylce rychlosti >15 % od rolling baseline. Využívají frozen baseline (nekontaminuje se anomálními vzorky) a hysterézi 5 % (zabraňuje flickeringu).

Po dokončení testu `AnomalyAnalysisService`:
- Páruje write a read anomálie na stejné pozici disku → **překryvné porovnání**.
- Počítá korelaci (0–100) podle pozice, odchylky, trvání a směru → ≥70 = pravděpodobná fyzická vada.
- Detekuje opakující se vzory na stejné pozici.
- Generuje lidsky čitelný report (součást certifikátu).
- Počítá penalizaci 0–50 bodů do celkového skóre disku.

### SMART historické trendy

Při každém testu se automaticky ukládá SMART snapshot do tabulky `SmartSnapshots`. Služba `SmartTrendService` poté:

- Agreguje všechny snapshoty pro daný disk.
- Počítá lineární regresi pro klíčové metriky (teplota, reallocated sectors, wear leveling, percentage used, pending sectors, uncorrectable errors, media errors, unsafe shutdowns, power-on hours, available spare).
- Vypočítává rychlost změny za den a predikuje dny do kritické meze.
- Generuje lidsky čitelný souhrnný report.
- Poskytuje data pro vykreslení trendových grafů v analytickém pracovišti.

### Vendor-specific hodnocení opotřebení SSD

`VendorWearMapping` obsahuje mapování pro 20+ výrobců SSD. Každý záznam specifikuje:

- **Semantiku normalizované hodnoty** – většina výrobců používá klesající škálu 100→0 (100 = nový, 0 = mrtvý).
- **Raw hodnotu** – průměrný počet mazacích cyklů NAND bloků.
- **Prahové hodnoty** pro varování (≤30) a kritický stav (≤10).

Pro NVMe disky se automaticky používá standardizovaný atribut `PercentageUsed` (0–100 % opotřebení).

### Režimy zálohy

| Režim | Popis | Použití |
|-------|-------|---------|
| **Souborová** | Kopíruje vybrané složky a soubory | Běžná záloha dat |
| **RAW obraz** | Čte sektory přímo z disku (1 MiB bloky) | Kompletní bitová kopie disku |
| **VHDx dynamický** | Vytváří standardní VHDx obraz (Microsoft formát) | Mountovatelný ve Windows i Linuxu, roste podle dat |

Všechny režimy jsou odolné vůči chybám čtení – nečitelné sektory jsou nahrazeny nulami a záloha pokračuje. Při více než 64 po sobě jdoucích chybách se operace přeruší (ochrana proti katastrofickému selhání disku).

### SMART – negativní cache

Pokud disk nebo adaptér nepodporuje SMART, provider uloží sentinel do cache na 30 minut. Při dalším dotazu se okamžitě vrátí `null` bez opakovaného volání `smartctl` – UI nezamrzá a testy nejsou zdržovány timeouty.

## Struktura repozitáře

```text
DiskChecker/
├── DiskChecker.Core/              # Doménové modely, rozhraní, AdaptiveSpeedSampler, AnomalyAnalysisService, QualityCalculator, SmartTrendService, VendorWearMapping
├── DiskChecker.Infrastructure/    # Platformní implementace, SMART, testy, sanitizace, SQLite, SchemaCompatibilityPatcher
├── DiskChecker.Application/       # Aplikační služby a obchodní logika
├── DiskChecker.UI.Avalonia/       # Hlavní desktopové UI
│   ├── ViewModels/                # MVVM view modely (30+ obrazovek)
│   ├── Views/                     # Avalonia AXAML views
│   ├── Services/                  # Navigace, dialogy, zálohy, lokalizace, stav dokumentů
│   ├── Converters/                # UI konvertory
│   ├── Locales/                   # cs/en překlady
│   └── Assets/                    # Ikony a assety
├── DiskChecker.TUI/               # Terminálový/experimentální projekt
├── tests/DiskChecker.Tests/       # Unit testy (190 testů)
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
- Před destruktivním testem vždy ověřte, že máte platnou zálohu dat.

## Testování

```bash
dotnet test
```

Testovací projekt (190 testů) pokrývá:
- Adaptivní vzorkování a detekci anomálií (11 testů)
- Analýzu anomálií – překryvy, korelace, penalizace, reporty
- Generování certifikátů
- Identitu disků
- Sanitizační progress
- Seek testy
- Nastavení
- SMART parsery a cache
- SMART detekci podpory

## Licence

Viz soubor [`LICENSE`](LICENSE).
