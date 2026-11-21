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
