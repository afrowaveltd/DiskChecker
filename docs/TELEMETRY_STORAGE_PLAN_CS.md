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
