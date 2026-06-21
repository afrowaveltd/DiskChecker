# DiskChecker – technická architektura

Tento dokument shrnuje aktuální technickou strukturu aplikace po posledních změnách. README popisuje projekt z vyšší úrovně, zatímco tento dokument slouží jako rychlá mapa pro vývojáře.

## Cíle architektury

DiskChecker je navržený jako servisní nástroj s oddělením domény, aplikačních scénářů, infrastruktury a UI:

- **Core** definuje modely a kontrakty bez závislosti na UI.
- **Application** skládá jednotlivé use-cases a pracuje s doménovými modely.
- **Infrastructure** řeší operační systém, hardware, databázi a externí nástroje.
- **UI.Avalonia** poskytuje desktopové workflow, navigaci a vizualizace.

```text
UI.Avalonia  ──>  Application  ──>  Infrastructure
     │                 │                  │
     └─────────────────┴──────────────────▼
                         Core
```

## DiskChecker.Core

`DiskChecker.Core` obsahuje sdílený doménový jazyk aplikace.

### Důležité modely

- `CoreDriveInfo` – identita a technické informace o fyzickém disku.
- `SmartaData`, `SmartaAttributeItem`, `SmartCheckResult` – SMART/SMARTA data a jejich interpretace.
- `DiskCard` – servisní karta disku identifikovaná hlavně sériovým číslem.
- `TestSession` – jednotný záznam testu včetně SMART snapshotů, rychlostí, teplot, chyb, výsledku, skóre a vazby na certifikát.
- `DiskCertificate` – metadata certifikátu, výsledek testu, atributy a cesta k exportu.
- `SurfaceTestRequest/Result/Sample` – kontrakt pro povrchové testy.
- `SeekTestRequest/Result/Recommendation` – kontrakt pro test latencí přeskoků.
- `EmailSettings` – SMTP konfigurace.
- `QualityRating`, `QualityGrade` – hodnocení zdraví.

### Důležitá rozhraní

- `ISmartaProvider`, `IAdvancedSmartaProvider` – čtení SMART dat, self-testy, atributy a maintenance akce.
- `IDiskDetectionService` – enumerace fyzických disků.
- `ISurfaceTestExecutor`, `ISurfaceTestService` – provedení a orchestrace povrchového testu.
- `ISeekTestExecutor` – provedení seek testu.
- `IDiskSanitizationService` – destruktivní sanitizace disku.
- `IDiskCardRepository` – perzistence diskových karet a testovacích session.
- `IDiskComparisonService` – porovnání disků.
- `IQualityCalculator` – výpočet známky/skóre.
- `IEmailSender`, `IEmailSettingsService`, `IReportEmailService` – e-mailové scénáře.

### Core services

- `QualityCalculator` – centrální výpočet kvality disku z dostupných dat.
- `SurfaceTestProfileDefaults` – výchozí profily povrchových testů.

Registrace probíhá přes `AddCoreServices()`.

## DiskChecker.Infrastructure

Infrastructure vrstva obsahuje vše, co je platformní, databázové nebo závislé na externích API/nástrojích.

### Hardware a operační systém

- `WindowsSmartaProvider` / `LinuxSmartaProvider`
  - implementují `ISmartaProvider` a `IAdvancedSmartaProvider`;
  - pracují se SMART daty, atributy, self-testy a logy;
  - podporují cache TTL přes `SmartaCacheOptions`.
- `DiskDetectionService` / `LinuxDiskDetectionService`
  - vrací `CoreDriveInfo` kolekci;
  - doplňují informace o připojení a rychlosti, pokud jsou dostupné.
- `VolumeInfoHelper` / `LinuxVolumeInfoHelper`
  - mapování fyzických disků na svazky, souborové systémy a informace o systémovém disku.
- `SeekTestExecutor`
  - nízkoúrovňové čtení z disku a sběr latencí.
- `SurfaceTestExecutor`, `DiskSurfaceTestExecutor`, `SequentialFileTestExecutor`, `SurfaceTestExecutorFactory`
  - různé strategie povrchového testu.
- `WindowsDiskSanitizationService` / `LinuxDiskSanitizationService`
  - destruktivní zápis, ověření čtením, volitelné vytvoření oddílu a formátování.
- `MetricsCollector`
  - sběr rychlostí, teplot, chyb a finální kompletace `TestSession`.

### SMART parsování

- `SmartctlJsonParser` – parsování JSON výstupu `smartctl`.
- `WindowsSmartJsonParser` – parsování Windows specifických SMART výstupů.

### Persistence

`DiskCheckerDbContext` používá EF Core a SQLite. Obsahuje:

- legacy tabulky: `DriveRecords`, `TestRecords`, `SmartaRecords`, `SurfaceTestSamples`;
- nové servisní tabulky: `DiskCards`, `TestSessions`, `DiskCertificates`, `DiskArchives`;
- nastavení a pomocné tabulky: `EmailSettings`, `ReplicationQueue`.

Při startu hlavní aplikace se databáze vytváří přes `EnsureCreated()` a následně se aplikuje `SchemaCompatibilityPatcher`, který pomáhá starším databázím dohnat aktuální schéma.

### Další služby

- `DiskCardRepository` – ukládání a dotazování karet disků, session, certifikátů.
- `CertificateGenerator` – generování certifikátových výstupů.
- `DiskComparisonService` – porovnávání diskových karet/metrik.

## DiskChecker.Application

Application vrstva skládá infrastrukturu a doménu do konkrétních scénářů.

### Klíčové služby

- `SmartCheckService`
  - spouští SMART kontrolu;
  - zpřístupňuje atributy, self-testy, logy a maintenance akce;
  - umí vrátit instrukce k závislostem a zkusit jejich instalaci.
- `DiskCardTestService`
  - vytváří/načítá kartu disku;
  - ukládá SMART check, surface test a sanitizaci jako `TestSession`;
  - počítá skóre, známku a health assessment.
- `SurfaceTestService` + `SurfaceTestPersistenceService`
  - orchestrace povrchového testu a uložení výsledků.
- `SeekTestService`
  - doporučení a provedení seek testu.
- `ReportGenerationService`, `TestReportAnalysisService`, `TestHistoryService`, `HistoryService`
  - reporty, analytika, historie a filtrování výsledků.
- `CertificateExportService`, `CertificationService`
  - tvorba/export certifikátů.
- `DatabaseMaintenanceService`, `DiskHistoryArchiveService`
  - údržba a archivace historie.
- `EmailSettingsService`, `SmtpEmailSender`, `ReportEmailService`, `TestCompletionNotificationService`
  - SMTP nastavení, odesílání e-mailů a notifikace po testech.
- `DriveIdentityResolver`
  - normalizace sériových čísel a stabilní identita disku.
- `SettingsService`
  - aplikační nastavení dostupná přes `ISettingsService`.

## DiskChecker.UI.Avalonia

Hlavní UI je desktopová aplikace v Avalonia. DI je nakonfigurováno v `App.axaml.cs`.

### Start aplikace

1. `Program.Main()` spustí Avalonia desktop lifetime.
2. `App.OnFrameworkInitializationCompleted()`:
   - vypne duplicitní Avalonia data validation pluginy;
   - na Windows zapne prevenci uspání systému během běhu aplikace;
   - sestaví `ServiceCollection`;
   - inicializuje SQLite databázi;
   - vytvoří `MainWindow` a připojí `MainWindowViewModel`.

### Navigace a obrazovky

`MainWindowViewModel` používá `INavigationService` a naviguje do těchto hlavních částí:

- výběr disku (`DiskSelectionViewModel`),
- karty disků (`DiskCardsViewModel`, `DiskCardDetailViewModel`),
- SMART check (`SmartCheckViewModel`),
- povrchový test (`SurfaceTestViewModel`),
- seek test (`SeekTestViewModel`),
- absolutní destruktivní test (`AbsoluteDestructiveTestViewModel`),
- bezpečný destruktivní test (`SafeDestructiveTestViewModel`),
- analýza (`AnalysisViewModel`),
- porovnání disků (`DiskComparisonViewModel`),
- reporty (`ReportViewModel`, `FullReportViewerViewModel`),
- historie (`HistoryViewModel`),
- certifikáty (`CertificateViewModel`, `CertificateBrowserViewModel`),
- záloha/obnova (`BackupViewModel`, `RestoreViewModel`),
- nastavení (`SettingsViewModel`).

### UI služby

- `NavigationService` – přepínání view modelů.
- `DialogService` – dialogy, potvrzení, zprávy a vstupy.
- `BackupService` – zálohování a obnova dat.
- `DiskCacheService` – cache seznamu disků mezi navigacemi.
- `SelectedDiskService` – sdílený aktuálně vybraný disk.
- `LocalizationService`/`LocaleService` – práce s lokalizací.
- `ReportDocumentState` – sdílený stav pro report preview/print.
- `DocumentLauncher` – otevření exportovaných dokumentů.

## Databázový model – praktický pohled

Nejdůležitější vazby:

```text
DiskCard 1 ── * TestSession
DiskCard 1 ── * DiskCertificate
TestSession 1 ── * owned TemperatureSample
TestSession 1 ── * owned SpeedSample(write/read)
TestSession 1 ── * owned SmartAttributeChange
TestSession 1 ── * owned TestError
DiskCertificate 1 ── * owned CertificateSmartAttribute
```

`DiskCard.SerialNumber` má unikátní index. `TestSession.SessionId` a `DiskCertificate.CertificateNumber` jsou také unikátní.

## Platformní rozdíly

### Windows

- Spuštění vyžaduje administrátorská práva.
- Manifest aplikace je součástí `DiskChecker.UI.Avalonia`.
- Fyzické disky používají cesty typu `\\.\PhysicalDriveN`.
- Sanitizace umí po dokončení vytvořit oddíl a formátovat na NTFS podle implementace.

### Linux

- Doporučené spuštění je přes `sudo` nebo vhodně nastavená oprávnění k blokovým zařízením.
- SMART data obvykle vyžadují `smartmontools`.
- Cesty zařízení jsou např. `/dev/sda` nebo `/dev/nvme0n1`.
- Sanitizace může po dokončení vytvořit oddíl a formátovat typicky na ext4.

## Konfigurace DI ve zkratce

V `App.axaml.cs` se registruje zejména:

- logging;
- `AddCoreServices()`;
- `DiskCheckerDbContext` se SQLite connection stringem `Data Source=DiskChecker.db`;
- aplikační služby pro historii, SMART, karty, certifikáty, testy, reporty, e-mail a nastavení;
- UI služby pro navigaci, dialogy, backup, lokalizaci a sdílený stav;
- platformní implementace:
  - Linux: `LinuxDiskDetectionService`, `LinuxSmartaProvider`, `LinuxDiskSanitizationService`;
  - ostatní/Windows: `DiskDetectionService`, `WindowsSmartaProvider`, `WindowsDiskSanitizationService`;
- view modely všech obrazovek.

## Build, publish a packaging

- Hlavní řešení je `DiskChecker.slnx`.
- Hlavní desktop projekt je `DiskChecker.UI.Avalonia`.
- `build.sh` provádí restore, Release build a publish pro `win-x64`, `linux-x64`, `linux-arm64`.
- `package.sh` očekává publish výstupy a generuje linux tarball/DEB balíčky.
- `installer/` obsahuje podklady pro Linux desktop soubor, DEB control/postinst, RPM spec a Inno Setup (`DiskChecker.iss`).

## Testy

`tests/DiskChecker.Tests` používají xUnit v3 a NSubstitute. Aktuálně pokrývají mimo jiné:

- analýzu reportů,
- generování certifikátů,
- resolver identity disku,
- sanitizační progress,
- seek testy,
- nastavení,
- SMART parsery a cache providerů.

## Riziková místa a poznámky pro vývoj

- Destruktivní operace musí být v UI vždy jasně potvrzené a navázané na konkrétní identitu disku.
- U externích USB adaptérů nemusí být SMART data dostupná nebo úplná.
- Avalonia balíčky jsou záměrně držené na 11.3.12 kvůli kompatibilitě s LiveCharts.
- `DiskChecker.TUI` existuje v repozitáři, ale není součástí hlavního `.slnx` workflow.
- Databázové změny je vhodné doprovodit aktualizací `SchemaCompatibilityPatcher`.
