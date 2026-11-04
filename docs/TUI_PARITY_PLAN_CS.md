# Plán parity terminálové verze (DiskChecker.TUI) s Avalonia aplikací

> Stavový dokument pro přestavbu `DiskChecker.TUI` na plnohodnotnou terminálovou obdobu desktopové aplikace.
> Cíl: stejné funkce, stejné datové zdroje, stejná pravidla a stejné uživatelské postupy – jen v terminálu.

---

## 1. Výchozí stav (analýza)

### 1.1 Avalonia aplikace (vzor)

Navigace v `MainWindow.axaml` (15 obrazovek + nastavení):

| # | Obrazovka | ViewModel | Klíčové funkce |
|---|-----------|-----------|----------------|
| 1 | Výběr disků | `DiskSelectionViewModel` | detekce disků, SMART grading, teplota, svazky, zámky, historie testů, dashboard |
| 2 | Karty disků | `DiskCardsViewModel` | seznam servisních karet, filtry, stránkování, archivace |
| 3 | Detail karty | `DiskCardDetailViewModel` | session, certifikáty, poznámky, SMART snapshoty, trendy |
| 4 | Test povrchu | `SurfaceTestViewModel` | profily HDD/SSD/NVMe, operace, živý průběh, grafy, uložení |
| 5 | SMART kontrola | `SmartCheckViewModel` | atributy, self-testy s pollingem, raw smartctl, cache, SMART certifikát |
| 6 | Seek test | `SeekTestViewModel` | doporučení dle SMART, konfigurace, statistiky (P95/P99), grafy |
| 7 | Absolutní destruktivní test | `AbsoluteDestructiveTestViewModel` | 8 fází, 2× sanitizace, 3× seek, SMART před/po, certifikát |
| 8 | Bezpečný destruktivní test | `SafeDestructiveTestViewModel` | záloha → test → obnova, SHA-256, nouzová obnova |
| 9 | Analýza | `AnalysisViewModel` | session, telemetrie, anomálie, stally, SMART trendy |
| 10 | Porovnání disků | `DiskComparisonViewModel` | metriky dvou a více disků |
| 11 | Report | `ReportViewModel` | generování reportu, náhled, export |
| 12 | Historie | `HistoryViewModel` | filtry, detail session |
| 13 | Certifikáty | `CertificateBrowserViewModel` + `CertificateViewModel` | prohlížeč, export PDF/JPEG/PNG, štítky |
| 14 | Záloha | `BackupViewModel` | souborová / RAW / VHDX, ověření obrazu |
| 15 | Obnova | `RestoreViewModel` | nalezení záloh, obnova, SHA ověření |
| 16 | Nastavení | `SettingsViewModel` + `ExportImportViewModel` | nastavení, DB backend, ZIP záloha dat, export/import JSON |

Sdílené vrstvy, které TUI může použít beze změny:
`DiskChecker.Core` (modely, rozhraní, `QualityCalculator`, `SmartTrendService`, `AdaptiveSpeedSampler`, `AnomalyAnalysisService`),
`DiskChecker.Application` (use-casy: `DiskCardTestService`, `SurfaceTestService`, `SeekTestService`, `TestAnalysisDataService`,
`CertificateExportService`, `ReportGenerationService`, `SettingsService`, …),
`DiskChecker.Infrastructure` (SMART provideři Win/Linux, detekce disků, sanitizace, seek/surface executory, EF Core SQLite, certifikáty).

### 1.2 Současná terminálová verze

`DiskChecker.TUI` (cca 1 570 řádků) je izolovaný experiment:

- cílení `net8.0-windows` + vlastní P/Invoke, **nesdílí** žádnou vrstvu aplikace,
- vlastní SQLite schéma (`TuiDbContext`) nekompatibilní s hlavní databází,
- 4 položky menu (test / historie / přehled / o aplikaci),
- jediný test = zápis+čtení+seek+sanitizace v jednom bloku bez fází a bez reportu,
- žádné SMART, karty disků, certifikáty, analýza, zálohy, nastavení, lokalizace, testy.

**Závěr:** terminálová verze se nepřestavuje kosmeticky, ale znovu na sdílených vrstvách.

---

## 2. Cílový stav

1. `DiskChecker.TUI` cílí `net10.0` (cross-platform) a referencuje `Core`, `Application`, `Infrastructure`.
2. Stejná DI kompozice jako `App.axaml.cs` → stejné služby, stejná databáze (`DiskChecker.db`), stejná nastavení.
3. Všech 16 obrazovek dostupných z navigační lišty, včetně podřízených obrazovek (detail karty, certifikát).
4. Živé průběhy testů (Spectre `LiveDisplay`): fáze, procenta, MB/s, elapsed/ETA, teplota, grafy v reálném čase, možnost přerušit.
5. Lokalizace cs/en ze společných `Locales/*.json`, přepínání za běhu.
6. Testovatelnost: každá obrazovka závisí na `ITuiIo` rozhraní + DI službách → testy bez terminálu.
7. Vlastní testovací projekt `tests/DiskChecker.Tui.Tests` pokrývající formátování, grafy, navigaci a každou obrazovku.

---

## 3. Architektura TUI

```
DiskChecker.TUI/
  Program.cs                 CLI argumenty, bootstrap, spuštění shellu
  App/TuiHost.cs             DI graf (kopie App.axaml.cs + terminálové služby)
  App/TuiShell.cs            hlavní smyčka, hlavička/patička, navigační lišta
  App/TuiNavigator.cs        zásobník obrazovek + zámek navigace během testu
  Services/
    ITuiIo.cs, TuiIo.cs      abstrakce konzole (produkce = Spectre, testy = skript)
    TuiLocaleService.cs      ILocaleProvider nad Locales/*.json
    TuiDialogs.cs            info/confirm/error/prompt dialogy
    TuiFormats.cs            bajty, trvání, ETA, známky, barvy, teploty
    TuiCharts.cs             čárové/ sloupcové grafy (Canvas) + datové transformace
    TuiSelection.cs          vybraný disk (obdoba ISelectedDiskService)
    TuiDiskCache.cs          cache detekce disků
    TuiBackupService.cs      ZIP záloha DB + nastavení
    VhdxImageUtility.cs      zápis/čtení fixed VHDx
  Screens/                   jedna třída na obrazovku
  Widgets/                   tabulky, panely, timeline, badge
```

Obrazovka = `ITuiScreen`:
```csharp
public interface ITuiScreen
{
    string TitleKey { get; }
    Task ShowAsync(TuiContext ctx);   // vykreslí, obslouží akce, vrátí se při "Zpět"
}
```

---

## 4. Kroky implementace (každý krok ověřen testy)

| Krok | Obsah | Ověření |
|------|-------|---------|
| 0 | Převod projektu na net10.0, reference sdílených vrstev, Spectre 0.57, zařazení do `.slnx` | build + existující testy |
| 1 | Foundation: `ITuiIo`, formátování, grafy, lokalizace, dialogy, navigace, shell, DI | `DiskChecker.Tui.Tests`: formátování, grafy, lokalizace, navigace |
| 2 | Výběr disků (dashboard, karty, zámky, historie) | testy obrazovky se skriptovaným IO |
| 3 | SMART kontrola (atributy, self-testy, raw smartctl, cache, certifikát) | testy + integrační test s fake providerem |
| 4 | Test povrchu (profily, operace, Live průběh, grafy, uložení) | testy průběhu a persistence |
| 5 | Seek test (doporučení, statistiky, grafy) | testy statistik a doporučení |
| 6 | Absolutní destruktivní test (8 fází, certifikát, session) | testy stavového automatu s fake sanitizací |
| 7 | Bezpečný destruktivní test (záloha/test/obnova, SHA) | testy workflow a nouzové obnovy se souborovým zdrojem |
| 8 | Analýza (session, telemetrie, anomálie, SMART trendy) | testy načtení a renderu |
| 9 | Historie + karty disků + detail karty | testy filtrů, stránkování, detailu |
| 10 | Certifikáty (prohlížeč, detail, export) | testy exportu do dočasného adresáře |
| 11 | Report + porovnání disků | testy generování a renderu |
| 12 | Záloha + obnova (RAW/VHDX/souborová) | testy manifests a round-trip na temp souboru |
| 13 | Nastavení + export/import dat | testy persistence a merge strategií |
| 14 | Dokumentace, README, finální regrese celého řešení | `dotnet test` (Avalonia + TUI) |

Po každém kroku: `dotnet build` bez varování + `dotnet test` zelené (TUI i stávající sada).

---

## 5. Mapování Avalonia → TUI

| Avalonia | TUI |
|----------|-----|
| okno + `MainWindowViewModel` | `TuiShell` (hlavička, navigace, stavový řádek) |
| `INavigationService` | `TuiNavigator` (zásobník, zámek během testu) |
| `IDialogService` (okna) | `TuiDialogs` (Spectre prompt/panel) |
| `LocaleService` | `TuiLocaleService` (stejná JSON, stejné klíče) |
| LiveCharts grafy | `TuiCharts` (Canvas: čára, pás, histogram, sparkline) |
| `DataGrid` | `Table`/`TreeTable` + stránkování v promptu |
| tlačítka | číslované akce v promptu + klávesové zkratky |
| progress bary | `LiveDisplay` + `ProgressBar`/`BreakdownChart` |
| `MessageBoxWindow` | `Panel` + `ConfirmationPrompt` |

## 6. Co TUI záměrně nepřebírá

- vzhled (barvy, gradienty, ikony) – nahrazeno terminálovou paletou,
- závislost na Windows-only knihovnách v UI vrstvě,
- mrtvé vazby a nefunkční tlačítka Avalonia UI (v TUI se neimplementují nebo se implementují správně),
- samostatné `TuiDbContext` schéma – nahrazeno sdílenou databází `DiskChecker.db`.

## 7. Rizika

| Riziko | Opatření |
|--------|----------|
| Destruktivní testy v terminálu | explicitní potvrzení, zámek systémového disku, zámek navigace během testu |
| Různá šířka terminálu | layout se počítá z `Console.WindowWidth`, fallback 100 znaků |
| Emoji v některých terminálech | klíčové stavy mají i textovou podobu |
| Dlouhé testy | průběžný zápis do DB, možnost přerušit Ctrl+C, session se ukládá i po přerušení |
| Rozsah | kroky 0–14, každý samostatně testovaný |
---

## 8. Stav implementace (dokončeno)

Všech 15 kroků plánu je hotových a ověřených testy.

| Krok | Stav | Poznámka |
|------|------|----------|
| 0 – převod projektu | ✅ | `net10.0`, reference `Core`/`Application`/`Infrastructure`, Spectre.Console 0.57, zařazeno do `.slnx` |
| 1 – foundation | ✅ | `ITuiIo`, lokalizace, formátování, grafy, progres, dialogy, navigace, DI host, shell |
| 2 – výběr disků | ✅ | dashboard, tabulka disků se známkou a teplotou, zámky, historie, přechod na SMART |
| 3 – SMART kontrola | ✅ | atributy, self-testy s živým průběhem, self-test log, raw `smartctl`, cache, SMART certifikát |
| 4 – test povrchu | ✅ | reálné sekvenční čtení, rychlý vzorek, zápis s ověřením, sanitizace, uložení do historie |
| 5 – seek test | ✅ | doporučení podle SMART, 3 režimy + vlastní počet, statistiky, session + vzorky + certifikát |
| 6 – absolutní destruktivní test | ✅ | 8 fází, 2× sanitizace, 3× seek, SMART před/po, skóre, certifikát |
| 7 – bezpečný destruktivní test | ✅ | záloha → test → obnova, SHA-256, nouzová obnova, certifikát |
| 8 – analýza | ✅ | telemetrie, grafy propustnosti/seeku/teploty, anomálie a stally, zoom, SMART trendy |
| 9 – historie + karty + detail | ✅ | filtry, stránkování, archivace, detail session, export certifikátu |
| 10 – certifikáty | ✅ | prohlížeč všech certifikátů, dokument, PDF, komentář technika, přepínání session |
| 11 – report + porovnání | ✅ | seznam testů, plný report, PDF, porovnání 2–5 disků, doporučení |
| 12 – záloha + obnova | ✅ | RAW / fixed VHDx / souborová záloha, manifest, SHA-256, obnova s dvojím potvrzením |
| 13 – nastavení + export/import | ✅ | všechna nastavení, DB backend, test spojení, ZIP záloha, JSON export/import |
| 14 – dokumentace a regrese | ✅ | `docs/TUI_GUIDE_CS.md`, README, `scripts/verify-tui.sh`, finální běh obou testovacích sad |

### Ověření

- `dotnet build DiskChecker.slnx -c Release` – 0 chyb, 0 varování (varování jsou chyby).
- `dotnet test tests/DiskChecker.Tests` – 441/441.
- `dotnet test tests/DiskChecker.Tui.Tests` – 271/271.
- Terminálová verze běží na reálném hardware (detekce 11 disků, SMART kontrola SSD s hodnocením A,
  navigace, přepnutí jazyka, korektní ukončení).

### Co TUI oproti Avalonii záměrně vylepšuje

- Test povrchu provádí **reálné I/O** (sekvenční čtení, zápis s ověřením), nikoli simulaci jako desktop.
- Opravené vady přenesené z desktopu: potvrzovací dialog destruktivního testu zobrazuje konkrétní disk,
  certifikát se ukládá právě jednou, porovnání disků je funkční, mazání v reportu skutečně maže.
- Diagnostika infrastruktury (`Console.WriteLine` v SMART provideru) je v terminálu potlačená,
  takže nekazí vykreslování.
