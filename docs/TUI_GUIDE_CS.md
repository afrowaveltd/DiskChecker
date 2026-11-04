# DiskChecker – terminálová verze (TUI)

Terminálová verze je rovnocenná obdoba desktopové aplikace pro servisní stanice bez grafického
rozhraní, pro vzdálené přístupy přes SSH a pro automatizaci. Používá stejné vrstvy
(`DiskChecker.Core`, `DiskChecker.Application`, `DiskChecker.Infrastructure`), takže:

- čte a zapisuje **stejnou databázi** `DiskChecker.db` jako desktopová aplikace,
- používá **stejná nastavení** (`settings.json`, `locked_disks.json`),
- používá **stejné SMART providery**, sanitizaci, seek/surface executory a generátor certifikátů,
- nabízí **stejné lokalizované texty** (`Locales/cs.json`, `Locales/en.json`).

## Spuštění

```bash
dotnet run --project DiskChecker.TUI
# nebo publikovaný binární soubor
./dcheck
```

### Volby příkazové řádky

| Volba | Význam |
|-------|--------|
| `--db <cesta>` | databázový soubor; přepíše uložené nastavení databáze |
| `--lang cs\|en` | jazyk rozhraní |
| `--screen <id>` | obrazovka otevřená po startu |
| `--verbose` | podrobné logování |
| `-h`, `--help` | nápověda |
| `-v`, `--version` | verze |

Identifikátory obrazovek: `disks`, `cards`, `surface`, `smart`, `seek`, `destructive`,
`safe`, `analysis`, `comparison`, `report`, `history`, `certificates`, `backup`,
`restore`, `settings`, `about`.

### Skriptování

Bez interaktivního terminálu aplikace čte odpovědi ze standardního vstupu – vždy jedno číslo
(nebo klíč volby) na řádek. Po uzavření vstupu (EOF) se aplikace sama ukončí. Příklad: otevřít
obrazovku o aplikaci a vrátit se:

```bash
printf '0\n' | ./dcheck --screen about --db /tmp/diskchecker.db
```

## Obrazovky

| Obrazovka | Co umí |
|-----------|--------|
| Výběr disků | detekce fyzických disků, SMART známka, teplota, svazky, zámky, historie testů, dashboard |
| Karty disků | servisní karty, filtry, archivace, obnovení, certifikát, detail |
| Detail karty | historie testů, SMART snímky, certifikáty, poznámky, generování certifikátu |
| Test povrchu | reálné sekvenční čtení celého disku, rychlý vzorek, zápis s ověřením (destruktivní) a sanitizace |
| SMART kontrola | atributy, self-testy s živým průběhem, self-test log, raw `smartctl`, cache, SMART certifikát |
| Seek test | doporučení podle SMART, full-stroke / random / skip / vlastní počet, statistiky a uložení |
| Absolutní destruktivní test | 8 fází: 2× sanitizace, 3× seek, SMART před/po, certifikát |
| Bezpečný destruktivní test | záloha → test → obnova s ověřením SHA-256 a nouzovou obnovou |
| Analýza | telemetrie z historie, grafy propustnosti, seek latence, teploty, anomálie a stally, SMART trendy |
| Porovnání disků | žebříček 2–5 disků, dvoudiskové porovnání výkonu, doporučení |
| Report | seznam testů, vyhledávání, plný report, export certifikátu |
| Historie | filtry podle typu testu, mazání, detail s přechodem na certifikát |
| Certifikáty | prohlížeč certifikátů všech disků, export PDF, komentář technika |
| Záloha | RAW obraz / fixed VHDx / souborová záloha s manifestem a ověřením SHA-256 |
| Obnova | nalezení záloh, obnova na vybraný disk s dvojím potvrzením a ověřením |
| Nastavení | jazyk, e-mail, databáze, SMART cache, export/import dat, ZIP záloha aplikace |
| O aplikaci | informace o aplikaci, systému a databázi, přepnutí jazyka |

## Ovládání

- Volby se vybírají číslem v nabídce (v interaktivním režimu šipkami a Enterem).
- Každá obrazovka nabízí `☰ Navigace` a `↩ Zpět`.
- `Ctrl+C` přeruší právě běžící operaci; druhé stisknutí aplikaci ukončí.
- Během testu je navigace zablokovaná, dokud test neskončí nebo není přerušen.

## Bezpečnost

- Destruktivní operace (zápis na disk, sanitizace, obnova, absolutní a bezpečný destruktivní test)
  vždy vyžadují explicitní potvrzení a zobrazují přesnou cestu zařízení.
- Systémový disk je automaticky zamčen a nelze jej odemknout.
- Během operace s diskem je zablokovaná navigace.
- SMART self-test nelze spustit na systémovém disku.
- Bezpečný destruktivní test po sobě vždy obnoví data ze zálohy – i při přerušení nebo chybě.

## Testy

```bash
dotnet test tests/DiskChecker.Tui.Tests        # obrazovky, formátování, grafy, lokalizace
dotnet test                                     # celé řešení (desktop + terminál)
scripts/verify-tui.sh                           # build + oba testovací projekty + smoke run
```

Terminálové testy používají skriptovaný terminál (`ScriptedTuiIo`), takže nepotřebují TTY a lze je
spouštět v CI. Obrazovky se testují včetně nabízených akcí – test selže, pokud obrazovka nabídne
jinou akci, než test očekává.