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

## Konfigurace bez falešných voleb

Kontrola skutečných call-site prokázala, že auto-save interval,
`EmailSendOnlyForLongRunningTests`, SMART cache TTL a SMART probe timeout
dosud nikdo v běžném Avalonia provozu nečte. Jejich hodnoty zůstávají v
konfiguraci kvůli kompatibilitě s TUI/plánovaným využitím, ale GUI je
nenabízí jako funkční přepínače. Výběr výchozí exportní složky je nově
skutečně respektován v `ExportImportViewModel.BrowseExportPathAsync`.
Volba přikládání certifikátu do e-mailu je nyní synchronizována s
`IEmailSettingsService` (SMTP DB), který čte skutečná odesílací služba.

## Důvěrnost konfigurace

Protože `settings.json` může obsahovat přístupové údaje v SQL connection
stringu, dočasný soubor pro atomické přepsání je na Linuxu/macOS vytvořen
s právy `0600` (pouze vlastník). Tato kontrola je součástí regresního testu.

## Otevření složky pod sudo

`SettingsViewModel` používá společný `DocumentLauncher.OpenFolder`.
Stejně jako otevření PDF přes `OpenFile` přepne na Linuxu při běhu
pod `sudo` otevření ve správci souborů do grafické relace původního
uživatele (pokud je dostupná). Při nedostupném prostředí zobrazí
uživateli chybu namísto falešného úspěchu.

## E-mail / SMTP a obnova záloh (další audit)

- Skutečný odesílač používá MailKit, propojený s `IEmailSettingsService`; nastavení
  SMTP serveru, portu, TLS, jména, hesla, adresy odesílatele a příjemce je nyní v Avalonia.
  Tlačítko **Uložit a odeslat zkušební e-mail** volá reálné `IEmailSender`.
- Dřívější SMTP kód akceptoval libovolný certifikát a mohl přejít na
  nešifrovaný režim. Nyní vyžaduje ověřené TLS dle portu a nepovolí
  SMTP login bez TLS. Soukromá CA musí být přidána mezi důvěryhodné v OS.
- Po úspěšně dokončeném povrchovém čtení je PDF certifikát přiložen
  ke zprávě; dokončený test s chybami odesílá chybové výsledky,
  nikoli falešný úspěch. SMTP neúspěch nesmí změnit uložený výsledek disku.
- Vybraná záloha měla ve ViewModelu `SelectedBackup`, ale žádný ovládací
  prvek ji neuměl nastavit; **Obnovit ze zálohy** proto bylo nepoužitelné.
  Doplněn výběr v ComboBox a tlačítko pro obnovení seznamu. Bez výslovného
  výběru uživatele zůstává příkaz obnovy zakázán.
- SMTP heslo se do UI nenačítá; prázdný vstup jej zachovává. Je uloženo
  v lokální databázi konfigurace SMTP; chraňte soubor databáze a zálohy.
- Dočasná omezení: automatické odesílání je zapojeno pro dokončené
  povrchové testy; další druhy testů je třeba postupně sjednotit samostatně.
