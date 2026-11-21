# DiskChecker – Avalonia: dialogy, navigace a bezpečnost běžících operací

Stav: 21. listopadu 2026. Revize skutečného zdrojového kódu; CI a uživatelský vizuální smoke test jsou samostatné kroky. Není to certifikace fyzických diskových operací.

## Zjištěné a opravené chyby

1. **Prázdné popisky OK/Zrušit v dotazu na SSD pauzy.** `InputDialogWindow.axaml` navazoval tlačítka na `L[Common.OK]` a `L[Common.Cancel]`, ale `InputDialogViewModel` nemá `L`. Tlačítka nyní čtou `OkButtonText` a `CancelButtonText` z lokalizační služby s bezpečnými záložními texty. Oprava platí pro každý generický vstupní dialog.
2. **Obcházení ochrany navigace.** Horní panel blokoval odchod při běžícím I/O, ale lokální tlačítka Zpět šla přímo na `INavigationService.NavigateTo`. Navigační služba nyní před vytvořením nového DI scope kontroluje `INavigationActivity.IsNavigationBlocked`. Běžící ViewModel se tak neodpojí ani neuvolní kvůli jiné navigační cestě.
3. **Nemožnost získat potvrzení bez hlavního okna.** Neobsloužený dialog dříve po ne-modálním `Show()` vracel `OK` bez stisku tlačítka, takže i destruktivní potvrzení mohlo být chybně vyhodnoceno kladně. Nově vrací `Cancel` – operace nesmí pokračovat bez souhlasu.
4. **Vícejazyčná stavová zpráva.** Blokovaná navigace používá `Common.NavigationBlocked` v české a anglické lokalizaci.

## Sjednocený kontrakt

`INavigationActivity` používají aktuálně `SurfaceTest`, `SeekTest`, `AbsoluteDestructiveTest`, `SafeDestructiveTest`, `Backup` a `Restore`. Aktivity blokují odchod pouze za běhu operace; při ukončení nebo zrušení je možné navigovat. Zálohování chrání také fáze skenování a výpočtu a samostatný asynchronní převod RAW → VHDx, který neběží v `BackupPhase.Running`. `MainWindowViewModel` a `NavigationService` používají stejný kontrakt, takže nové obrazovky lze zapojit bez duplikace přepínače typů.

Jde o ochranu životního cyklu GUI. Samotné potvrzení, identita cílového disku, kontrola připojených oddílů a kontrola rozsahu destruktivního zápisu zůstávají nezávislými bezpečnostními podmínkami a nesmí se oslabit.

## Regresní a ruční kontroly

- Automatické testy: `NavigationServiceTests` – zablokování navigace bez vytvoření nového scope, po dokončení jediné uvolnění staré instance; `InputDialogButtonLabelsTests` – konkrétní popisky tlačítek pro existující lokalizace.
- Linux/Windows: spustit sanitizaci **pouze na obětovaném testovacím zařízení**; zkontrolovat čitelná tlačítka, vstup 0–60 sekund a že Zrušit nepokračuje do zápisu. Zkusit CZ/EN a světlé/tmavé téma.
- Během běhu testu, zálohování a obnovy kliknout na Zpět i globální menu: nesmí nastat změna pohledu ani zánik sledování průběhu. Po ukončení navigace opět funguje.
- Není k dispozici místní .NET SDK v prostředí autora těchto úprav: sestavení/testy vyhodnotit podle GitHub Actions a vizuální výsledek ověřit na cílovém PC.

## Další audit

Zbývá kompletní ruční projití všech pohledů a hardwarových scénářů. Opravený ochranný kontrakt **nenahrazuje** nezávislou bezpečnostní revizi restore a destruktivního testu. Knihovny Avalonia/LiveCharts/Skia zůstávají v dosud kompatibilních verzích; **Skia se kvůli této změně neaktualizuje**.

## Doplňkové ověření klíčů (celkem 24 hlavních AXAML obrazovek)

Porovnány odkazy `L[...]` ve všech hlavních oknech s českým a anglickým
slovníkem. Chyběly `Settings.ExportImport` a
`DiskCardDetailView.SeekLatency`; oba překlady byly doplněny.
U `Settings.ExportImport` se současně opravila vazba tlačítka na
`NavigateToExportImportCommand` přímo v `SettingsViewModel`, bez zbytečného
parent-selector výrazu. Kontrola zahrnovala pouze deklarované XAML
lokalizační klíče; dynamicky složené popisky a runtime navigaci je stále
třeba vizuálně ověřit.

## Nastavení: sjednocení s aplikačními službami (navazující etapa)

- XAML používá existující `AutoSaveInterval`, `SmartCacheTtlMinutes` a `SmartProbeTimeoutSeconds` místo nenapojených vlastností.
- Oba e-mailové přepínače a USB recovery retry se načítají a ukládají přes `ISettingsService`.
- Výběr exportní složky používá skutečný systémový picker a po zrušení zachová dosavadní cestu.
- `GoBack`, otevření datové složky a vymazání výhradně **paměťové** cache mají skutečné příkazy.
- Soubory databáze se nemažou. Velikost SQLite se čte pouze pro aktivní lokální backend; ostatní `N/A`.
- Nefunkční volby „výchozí test“, obecná upozornění a „automatická záloha“ byly skryty, protože zde zatím nemají perzistentní funkci. Funkční přepínače mají vždy odpovídající stav a úložiště.
- Po dokončení nutno vizuálně ověřit a projít regresní testy na Windows i Linuxu. Skia/Avalonia beze změny.

## Porovnání disků – skutečná data namísto mrtvých vazeb

Původní `DiskComparisonView.axaml` byl napsaný pro jiné rozhraní než existující
`DiskComparisonViewModel`. Výměna za pohled nad `AvailableDisks`, `SelectedDisks` a
`ComparisonResults` obnovuje výběr 2–5 disků, porovnání, historii skóre,
přehled výsledků a export CSV. Ovládací prvky používají existující příkazy
`AddDisk`, `RemoveDisk`, `CompareDisks`, `GoBack` a nový `ExportComparison`.
Po změně výběru se staré porovnání a grafy zahodí.

Skóre 0–100 a rychlosti MB/s mají nezávislé OxyPlot grafy. Nezměřené
fáze se zobrazí jako N/A a nevytváří falešnou rychlost 0 MB/s.
Export používá pouze uložené výsledky aktuálního porovnání; CSV
chrání text před tabulkovým formula-injection. Současně se odstranilo
rizikové dělení nulou v porovnání výkonu a výběr poslední session
respektuje čas testu. Hardwarové testy a Skia beze změny.

## Trvalost nastavení a bezpečný soubor

`SettingsService` již neztrácí běžné hodnoty (`AutoSaveInterval`,
`DefaultExportPath`, `Language`, logování a další) po restartu.
Ukládá je v jednom JSON souboru spolu se SMART/e-mailovými volbami.
Dočasný soubor a atomické přejmenování brání částečnému přepsání při
chybě I/O. Chybu uložení nyní volající obdrží; UI ji neskryje za
zavádějící zprávu „Uloženo“.
`SettingsViewModel` při operaci ukládání/zálohy/obnovy blokuje navigaci
stejným kontraktem jako ostatní obrazovky.

## Navazující regresní testy

`DiskComparisonUiTests` ověřuje výběr a odebrání disků, invalidaci výsledků,
samostatné grafy skóre/MB/s, rozlišení N/A a bezpečný CSV export.
`SettingsViewModelInteractionTests` ověřuje zrušení výběru složky,
skutečný výběr, mazání pouze paměťové cache a blokování navigace.
`SettingsServiceTests` ověřuje uchování voleb přes restart služby.
`DiskComparisonServiceRegressionTests` ověřuje skutečně poslední session
a srovnání bez dělení nulou. Výsledky CI jsou nutné před sloučením.
