# Plán: detailní telemetrie testů a volitelné DB úložiště

## Cíle

- Ukládat průběhová data tak, aby šla později analyzovat v kartě disku: zoom, časová osa, procentuální/prostorový průběh, anomálie a stally.
- Zachovat rozumnou velikost DB pomocí inteligentní redukce vzorků místo prostého „každý n-tý bod“.
- Umožnit volbu databázového backendu: SQLite pro lokální vývoj a PostgreSQL / SQL Server pro serióznější perzistenci.
- Protože je aplikace ve vývoji, preferujeme čistý a budoucí návrh před kompatibilitou starých testů.

## Fáze 1 – základ infrastruktury

1. Přidat nastavení persistence:
   - `DatabaseProvider`: `Sqlite`, `PostgreSql`, `SqlServer`
   - `DatabaseConnectionString`
   - UI výběr backendu a test spojení.
2. Konfigurovat EF DbContext podle nastavení při startu aplikace.
3. Přidat EF provider balíčky pro PostgreSQL a SQL Server.
4. Prozatím používat `EnsureCreated()`; později přejít na migrace nebo řízené resetování schématu.

## Fáze 2 – datový model telemetrie

Rozšířit ukládanou telemetrii tak, aby každý vzorek měl čas i prostor:

- `Timestamp`
- `ElapsedMs` / odvozeně z `StartedAt`
- `ProgressPercent`
- `BytesProcessed`
- `SpeedMBps`
- `IsStalled`
- fáze testu (`Write`, `Read`, `Verify`, `Sanitize1`, `Sanitize2`, ...)
- příznaky anomálie / důvod zachování vzorku

Doporučené nové události:

- `TestStallEvent`: začátek/konec/doba/progress/bytes/fáze
- `TestAnomalyEvent`: typ, severity, baseline, min/max, časový i procentuální rozsah

## Fáze 3 – retention/reducer

Přidat službu `SpeedSampleRetentionService`, která z raw vzorků vytvoří persistentní sadu.

Profily:

| Profil | Cíl vzorků / fáze | Použití |
|---|---:|---|
| Compact | ~1000 | běžná instalace |
| Balanced | ~3000 | default |
| Research | ~10000 | detailní výzkum |

Pravidla zachování:

- první nenulový vzorek
- poslední vzorek
- každý stall + okolí před/po
- lokální minima/maxima
- změna rychlosti větší než adaptivní práh
- začátek/konec stabilního úseku
- vzorky okolo chyb/anomálií

Adaptivní práh:

```text
threshold = max(0.5 až 1 MB/s, avgSpeed * 1–3 %)
```

## Fáze 4 – analytické UI

V kartě disku přidat detail testu:

- graf rychlost podle procenta disku
- graf rychlost podle času
- vyznačení stall intervalů
- vyznačení anomálií
- zoom vybrané oblasti
- detail vzorků a export CSV/JSON

## Fáze 5 – certifikáty a reporty

Certifikát používat jako shrnutí, ne jako hlavní úložiště detailů. PDF má dostat data z telemetrie nebo z agregovaných fallback hodnot.

## Doplnění: kompletní seek testy

Seek testy mají typicky do ~3000 kroků, proto je ukládáme kompletně bez redukce. Cílem je pozdější detailní analýza podle typu seeku, vzdálenosti, LBA pozic, latence a chyb.

Nová perzistence:

- samostatná tabulka `SeekSamples`
- vazba na `TestSessionId`
- typ seek testu (`FullStroke`, `Random`, `Skip`)
- index, source/destination LBA, seek distance
- latency ms, timestamp UTC
- error flag + message

Další krok po telemetrii:

- analytické pracoviště nad kartami disků
- timeline testů
- porovnání historických SMART snapshotů
- vývoj normalizační vrstvy pro SSD wear indikátory podle vendorů

## Implementováno nyní: TestTelemetrySamples

Přidána samostatná tabulka `TestTelemetrySamples` pro throughput telemetrii. Zatím se do ní automaticky kopírují uložené write/read `SpeedSample` kolekce při vytvoření `TestSession`. Legacy owned kolekce zůstávají zachované kvůli aktuálním obrazovkám a certifikátům. Další krok je přidat reducer/retention profily a postupně přesměrovat analytické UI na tuto tabulku.

## Implementováno nyní: SpeedSampleRetentionService

Přidána služba `SpeedSampleRetentionService` s profily `Compact`, `Balanced`, `Research`. Repository nyní při plnění `TestTelemetrySamples` používá profil `Balanced`: zachová první/poslední bod, jednotný baseline, stally s okolím, významné změny rychlosti a lokální minima/maxima. Legacy owned vzorky zatím zůstávají beze změny kvůli existujícím obrazovkám; analytická tabulka už je redukovaná a vhodnější pro dlouhodobé ukládání.

## Implementováno nyní: přesné RetentionReason

`SpeedSampleRetentionService` nyní kromě redukovaných vzorků vrací i důvod zachování každého bodu (`First`, `Last`, `Baseline`, `SpeedChange`, `SpeedChangeContext`, `LocalMin`, `LocalMax`, `ExtremaContext`, `Stall`, `StallContext`, případně trim důvody). `DiskCardRepository` tyto důvody ukládá do `TestTelemetrySamples.RetentionReason`, takže budoucí analytické UI bude umět zvýraznit, proč je bod důležitý.

## Implementováno nyní: TestAnomalyEvents

Přidána tabulka `TestAnomalyEvents` pro souhrny detekovaných výkonových anomálií. Při vytvoření `TestSession` se `AnomaliesJson` deserializuje přes `TestSession.Anomalies`, uloží se eventy a high-resolution anomaly samples se doplní do `TestTelemetrySamples` s `IsAnomaly=true` a `RetentionReason=Anomaly` / `Anomaly+Stall`.

## Implementováno nyní: TestAnalysisDataService

Přidána servisní vrstva `ITestAnalysisDataService` / `TestAnalysisDataService`, která skládá budoucímu analytickému UI kompletní datový balíček: metadata `TestSession`, `TestTelemetrySamples`, `TestAnomalyEvents`, `SeekSamples` a teplotní vzorky. Přidán také `TestAnalysisSummary` pro budoucí seznam měření v kartě disku / analytickém pracovišti.

## Implementováno nyní: TestStallEvents

Přidána tabulka `TestStallEvents` pro intervaly zamrznutí zařízení. Při ukládání `TestTelemetrySamples` se ze stall bodů odvozují intervaly se začátkem/koncem, délkou v ms, progress rozsahem, bytes processed a rychlostí před/po. `TestAnalysisDataService` nyní vrací i stall eventy a summary obsahuje `StallCount`.

## Návrh analytického pracoviště: dva režimy zobrazení

Analytické pracoviště bude mít přepínač režimu zobrazení, aby bylo použitelné jak na menším notebooku, tak na velkém servisním monitoru.

### Režim 1: Kompaktní / menší obrazovka

Cíl: rychle zobrazit nejdůležitější informace bez zahlcení UI.

Doporučené automatické zobrazení:

1. Horní souhrnný panel
   - disk model + serial
   - typ testu
   - datum / délka testu
   - známka / skóre
   - počet anomálií
   - počet stallů
   - počet seek vzorků, pokud existují

2. Jeden hlavní graf podle typu testu
   - pro write/read/sanitization: kombinovaný throughput graf podle procent disku
   - pro seek test: latency graf podle indexu seeku
   - pro test se stally: zvýraznit stall intervaly v grafu

3. Automatická redukce detailů
   - zobrazit maximálně jeden primární graf
   - tabulku anomálií zobrazit jako stručný seznam top 5 podle severity
   - seek data pouze jako souhrn + tlačítko „detail“
   - SMART historie pouze kompaktní „před/po“ nebo nejdůležitější změny

4. Interakce
   - klik na anomálii přesune hlavní graf na danou oblast
   - tlačítko „Zobrazit detail“ otevře podrobnější panel nebo přepne do komplexního režimu

Vhodné pro:

- notebook
- menší okno
- rychlé servisní rozhodnutí
- generování certifikátu/reportu

### Režim 2: Komplexní / velký monitor

Cíl: analytické pracoviště pro detailní výzkum a porovnávání disků.

Doporučené automatické zobrazení:

1. Levý panel: strom / seznam
   - diskové karty
   - test sessions
   - filtry podle typu testu, data, známky, anomálií, stallů

2. Horní souhrn vybraného testu
   - metadata disku
   - typ testu
   - skóre / známka
   - agregované rychlosti AVG/MIN/MAX
   - počet a celková délka stallů
   - počet anomálií + max severity
   - SMART delta summary

3. Hlavní grafy ve více panelech
   - throughput podle procenta disku
   - throughput podle času
   - stall timeline
   - teplota podle času / progressu
   - anomaly overlay
   - seek latency podle indexu
   - seek latency podle vzdálenosti LBA

4. Pravý panel detailů
   - vybraná anomálie
   - vybraný stall interval
   - vybraný seek vzorek
   - SMART atributy před/po
   - poznámky a doporučení

5. Dolní panel / tabulky
   - tabulka anomálií
   - tabulka stallů
   - tabulka seek vzorků
   - export CSV/JSON

6. Interakce
   - zoom v grafech
   - synchronizovaný kurzor mezi grafem času a grafem progressu
   - klik na anomálii/stall zvýrazní oblast ve všech grafech
   - možnost porovnat více testů stejného disku
   - možnost porovnat více disků

Vhodné pro:

- servisní stanici
- velký monitor
- výzkumné srovnávání disků
- hledání slabých oblastí média

### Automatická volba režimu

Aplikace může režim vybrat automaticky podle aktuální šířky okna, ale uživatel musí mít možnost volbu přepsat ručně.

Navržené pravidlo:

- šířka okna < 1400 px: Kompaktní režim
- šířka okna >= 1400 px: Komplexní režim
- ruční volba v nastavení: `Auto`, `Compact`, `Full`

### Budoucí nastavení

Navržený enum:

```csharp
public enum AnalysisWorkspaceMode
{
    Auto = 0,
    Compact = 1,
    Full = 2
}
```

Navržené nastavení:

```csharp
Task<AnalysisWorkspaceMode> GetAnalysisWorkspaceModeAsync();
Task SetAnalysisWorkspaceModeAsync(AnalysisWorkspaceMode mode);
```

### Mapování dat podle režimu

| Oblast | Compact | Full |
|---|---|---|
| Souhrn testu | ano | ano, detailní |
| Throughput progress graf | ano | ano |
| Throughput time graf | ne automaticky | ano |
| Stall timeline | jen badge + overlay | samostatný graf |
| Anomálie | top 5 seznam | plná tabulka + overlay |
| Seek vzorky | souhrn | graf + tabulka |
| Teplota | badge/minigraf | samostatný graf |
| SMART delta | nejdůležitější změny | detailní před/po tabulka |
| Export | základ | CSV/JSON/PDF |

### Doporučené pořadí implementace UI

1. Přidat enum a nastavení režimu.
2. Vytvořit `AnalysisWorkspaceViewModel` nad `ITestAnalysisDataService`.
3. Udělat kompaktní režim jako první, protože bude jednodušší a okamžitě použitelný.
4. Přidat full režim s více grafy a tabulkami.
5. Doplnit synchronizovaný zoom a porovnávání více testů/disků.

## Implementováno nyní: LBA u anomálií a základní zoom

Anomálie nyní ukládají `StartBytesProcessed`, `EndBytesProcessed` a odvozené `StartLba512` / `EndLba512`. Tyto hodnoty jsou propagované do `TestAnomalyEvents`, indexované a zobrazené v detailu vybrané anomálie. V analytickém UI byl přidán základní progress zoom: klik na anomálii nebo stall nastaví rozsah grafu okolo události a `Reset zoom` vrátí celý rozsah.

## Implementováno nyní: SMART analýza

Přidán `SmartAnalysisReport` do `TestAnalysisData`. `TestAnalysisDataService` nyní vyhodnocuje SMART before/after snapshoty, změny kritických atributů a základní SSD wear heuristiky (`PercentageUsed`, `AvailableSpare`, `WearLevelingCount`). UI analytického pracoviště zobrazuje SMART summary a detail nejdůležitějších delta/wear položek. Interpretace SSD wear zůstává opatrná, protože vendor atributy nejsou jednotně normalizované.
