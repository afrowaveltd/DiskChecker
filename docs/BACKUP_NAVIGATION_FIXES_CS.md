# Opravy zálohování, VHDX a navigace (2026-10-26)

## 1. Diskové cíle zálohy v Linuxu

`DriveInfo.GetDrives()` v Linuxu vrací také virtuální přípojné body. Samotné vyřazení `CDRom` a `Ram` proto nestačilo a do běžného zálohování pronikaly například Snap obrazy.

Nová sdílená služba `LinuxBackupTargetDiscovery` zpracovává jeden stromový výstup:

```bash
lsblk --json --paths --output NAME,KNAME,PATH,TYPE,FSTYPE,RO,MOUNTPOINTS,MAJ:MIN
```

- Prochází pouze větve diskových zařízení; podporuje více úrovní oddíl → šifrování → LVM a RAID.
- Vyřazuje loop/Snap, optická a RAM/zram zařízení, swap, obrazové souborové systémy a zařízení s příznakem RO.
- Zabraňuje výběru zdrojového fyzického disku, jeho sourozeneckých oddílů a vnořených svazků. U RAID zohledňuje i sdílený svazek uvedený pod více členy.
- Deduplikuje podle `MAJ:MIN`; bind mounty a podsvazky stejného zařízení nenavyšují volnou kapacitu.
- Používá přesné cesty, včetně kořenového `/` a názvů s mezerami.

Stejný filtr používá `BackupViewModel` i `SafeDestructiveTestViewModel`. Následně se přes `DriveInfo` získá aktuální volná kapacita; nedostupný nebo mezitím odpojený cíl se přeskočí. Běžné zálohování při selhání detekce oznámí chybu inicializace, bezpečné workflow ponechá prázdný seznam cílů.

## 2. Přepínání obrazovek a SeekTest

Byly opraveny tři problémy životního cyklu:

1. **Sdílené série dvou grafů.** `BuildFinalChart` přiřazoval stejné instance sérií včetně Skia paintů živému i výsledkovému grafu. Každý graf nyní vlastní své série a osy; výsledkový graf se publikuje jedním přiřazením bez přechodných prázdných os a opožděného překreslovacího callbacku.
2. **Dobíhající asynchronní operace.** Po opuštění SeekTestu se ruší test i načítání doporučení. Opožděné výsledky, výjimky a již zařazené progress callbacky neaktualizují opuštěné view. Zrušovací zdroj uvolňuje operace ve svém `finally`, nikoliv navigace během práce executorů.
3. **Pořadí uvolnění navigačního scope.** `NavigationService` nejdříve připraví nový view model a oznámí výměnu view, teprve poté uvolní předchozí DI scope. Pokud vytvoření nebo inicializace cíle selže, původní view model zůstane použitelný. Uvolnění transientních view modelů zajišťuje DI právě jednou.

## 3. Připojitelný VHDX i pro běžné velikosti disků

Writer používá 1MiB payload bloky a 512B logické sektory. Jeden chunk obsahuje 4096 payload bloků (4 GiB). V BAT musí být mezi chunky vyhrazená položka pro sector bitmap, i když pevný obraz bitmapové bloky nealokuje. Původní writer zapisoval jen souvislou řadu payload položek: po první hranici 4 GiB standardní čtečka interpretovala jinou položku, než writer zamýšlel.

`VhdxImageUtility` nyní:

- počítá počet BAT položek jako `payloadBlocks + floor((payloadBlocks - 1) / 4096)`;
- vkládá nulové bitmapové položky mezi chunky a zahrnuje je do velikosti 1MiB zarovnané BAT oblasti;
- kontroluje stejné rozložení při čtení před obnovou;
- zapisuje obě redundantní hlavičky se shodnými FileWriteGuid a DataWriteGuid;
- ověřuje nejen délku souboru, ale i hlavičku, metadata a BAT, než zálohovací workflow ohlásí úspěch.

Běžná VHDX záloha i bezpečné workflow před validací flushnou a uzavřou výstupní stream. Po nahrazení nečitelného bloku nulami se další čtení zahájí na správném offsetu zdroje, aby se následující data neposunula. Manifesty přímé zálohy, bezpečného workflow i převodu RAW → VHDX uvádějí přesnou virtuální kapacitu, nikoli zaokrouhlenou velikost payloadu. Z běžného zálohovacího view modelu byl odstraněn nepoužívaný starý writer s neplatným rozložením.

UI a české i anglické README nyní správně označují formát jako **pevný VHDX**. Oprava se použije při vytváření nových obrazů včetně převodu RAW; již vytvořené vadné VHDX se automaticky nepřepisují. Jejich chybnou BAT validátor odmítne při obnově.

## 4. Regresní ověření

- `LinuxBackupTargetDiscoveryTests`: Snap/loop, zram, optická média, RO, obrazové FS, root, LVM/šifrování, duplicity, RAID a vyloučení zdroje.
- `NavigationServiceTests`: uvolnění původního scope až po výměně view a zachování původního view při selhání cíle.
- `SeekTestNavigationTests`: Avalonia Headless se skutečným Skia rendererem, 12 opakovaných přechodů z SeekTestu a vykreslení výsledkového grafu; odchod během testu a během SMART doporučení.
- `BackupRestoreImageTests`: původní round-trip testy a nové řídké obrazy přes hranice 4 GiB, 8 GiB a 128 GiB, včetně růstu BAT oblasti. `qemu-img` nezávisle otevře obraz a `qemu-io` ověří obsah sektorů u hranic a konce disku. Celý běžný zálohovací postup je navíc ověřen převodem přes QEMU zpět na RAW a porovnáním SHA-256.
- `BackupWorkflowUiTests`: převod RAW → VHDX a zálohovací fáze bezpečného workflow ověřují přesnou kapacitu a shodu payloadu; u bezpečného workflow také úspěšné hashové ověření před dokončením.

Spuštění celé sady:

```bash
dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj -c Release
```

Na Linuxu balíček `qemu-utils` poskytuje `qemu-img` a `qemu-io`. Bez QEMU se nezávislé testy hranic explicitně přeskočí. Headless renderer potřebuje dostupné fonty a fontconfig; CI tyto závislosti instaluje. Testy používají dočasné soubory a simulovaný executor, neprovádějí operace nad fyzickými disky.

V CI workflow byl také odstraněn duplicitní klíč `permissions`, aby konfigurace měla jednoznačnou YAML strukturu.

### Výsledek lokálního ověření

Release sestavení prošlo. Z 419 testů prošlo 394 v plném běhu; 25 testů certifikátů narazilo na oprávnění existující uživatelské složky `~/.config/DiskChecker/Certificates`. Všech těchto 25 testů následně prošlo s izolovanou konfigurační cestou:

```bash
XDG_CONFIG_HOME=/tmp/opencode dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CertificateGeneratorTests|FullyQualifiedName~CertificateExportServiceTests'
```

Všechny nové regresní testy včetně nezávislého čtení VHDX přes QEMU a headless UI prošly bez přeskočení. Snímek vykresleného výsledkového grafu byl rovněž vizuálně zkontrolován.

Volitelné uložení snímku vykresleného SeekTestu:

```bash
DISKCHECKER_UI_SCREENSHOT=/tmp/seek-navigation.png dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj --filter FullyQualifiedName~CompletedChartsCanBeRepeatedlyDetached
```
