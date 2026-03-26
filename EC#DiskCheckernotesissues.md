1. Problém s generováním výsledků testu (Windows trvá obrovskou dobu)

## 1. Problém s generováním výsledků testu (Windows trvá obrovskou dobu)

### Příčiny:
- WindowsSmartaProvider používá neefektivní přístup ke získávání SMART dat
- Každý dotaz na smartctl způsobí výrazné zpoždění kvůli:
  * Spuštění nového procesu pro každý dotaz
  * Parsování kompletního JSON výstupu i když potřebujeme jen několik hodnot
  * Žádná mezipaměť výsledků mezi voláními

### Řešení:
1. Implementovat efektivní mezipaměť SMART dat v WindowsSmartaProvider:
   - Přidat TTL-based cachování s výchozí dobou 10 minut (již částečně implementováno v ServiceCollectionExtensions.cs)
   - Zvýšit efektivitu parsování tím, že extrahujeme pouze požadované atributy
   - Přidávat předvyžádění pro často používané kombinace atributů

2. Optimalizovat volání smartctl:
   - Místo několika samostatných volání použít jedno komplexní volání s potřebnými argumenty
   - Použít smartctlovy možnosti pro výběr konkrétních polí (pokud jsou dostupné)
   - Využívat asynchronní zpracování kde je to možné

3. Přidat diagnostické sledování:
   - Logovat délku každého smartctl volání pro identifikaci úzkých hrdel
   - Měřit úspěšnost cachování (hit/miss ratio)
   - Upozorňovat při dlouhotrvajících operacích (>5 sekund)


## 2. V protokolu není správný graf, chybí veškeré SMART údaje (hodiny, realokace)

### Příčiny:
- SmartctlJsonParser neextrahuje všechny potřebné atributy z JSON výstupu
- V TestRecord.cs chybí pole pro ukládání specifických SMART atributů jako PowerOnHours, ReallocatedSectors apod.
- V generování certifikátu se nesprávně mapují hodnoty mezi session.SmartBefore a certificate properties

### Řešení:
1. Rozšířit SmartctlJsonParser pro extrakci všech kritických SMART atributů
   - Přidat parsování atributů 5 (ReallocatedSectorCount), 9 (PowerOnHours), 12 (PowerCycleCount), 197 (PendingSectorCount)
   - Zajistit správnou konverzi raw hodnot na interpretovatelné formy

2. Aktualizovat TestRecord model pro ukládání SMART dat
   - Přidat pole: PowerOnHours, PowerCycleCount, ReallocatedSectorCount, PendingSectorCount, Temperature
   - Aktualit migrační skripty databáze pro přidání těchto sloupců

3. Opravit mapování v CertificateGenerator.cs
   - Zajistit, aby se hodnoty ze SmartaRecord správně kopírovaly do DiskCertificate properties
   - Přidat validaci a fallback hodnoty pro případy, kdy SMART data nejsou dostupná

4. Vylepšit generování grafu v PDF reportu
   - Přidat více datových bodů pro plynulejší zobrazení
   - Zajistit, že graf zobrazuje reálné měřené hodnoty, ne jen interpolované
EOF

## 3. Jaké nesprávné výrobní číslo - je u všech disků stejné (placeholder?) a chyba je i v detekci zda byl disk již testován

### Příčiny:
- V DiskCardTestService.GetOrCreateCardAsync dochází k problémům s generováním identity klíče
- Funkce BuildIdentityKey nepřevádí správně cestu na diskový identifikátor
- SerialNumber může být prázdný nebo obsahovat nevěrohodné hodnoty, což vede k použití placeholderů
- Logika pro detekci již testovaných disků nesprávně porovnává identifikátory

### Řešení:
1. Opravit generování identity klíče v DriveIdentityResolver.cs:
   - Zajistit, že funkce BuildIdentityKey správně zvládá různé formáty cest (\\.\PhysicalDriveX, C:, D: atd.)
   - Přidat robustní parsování sériových čísel včetně odstranění spécuálních znaků
   - Implementovat fallback na název disku při chybějícím nebo neplatném sériovém čísle

2. Vylepšit detekci duplicitních disků v DiskCardTestService.GetOrCreateCardAsync:
   - Změnit logiku porovnávání na spolehlivější shodu více atributů (Model + SerialNumber + velikost)
   - Přidat váhové skórování pro různá kritéria shody
   - Implementovat časové okno pro považování testu za „nový“ (např. test starší než 24h je považován za nový)

3. Přidat normalizaci a validaci sériových čísel:
   - Vytvořit funkci NormalizeSerialNumber která odstraní běžné problémy (mezery, speciální znaky, rozdílná velikost písma)
   - Přidat kontrolu délky a formátu sériového čísla podle typu disku (SATA vs NVMe vs USB)
   - Logovat pokusy o použití neplatných nebo podezřelých sériových čísel

4. Zlepšit zpracování chybějících dat:
   - Místo použití pevných placeholderů (\"UNKNOWN\") používat rozpoznatelné indikátory důvěryhodnosti dat
   - Přidat metadata o spolehlivosti každého pole (zdroj dat, čas získání, počet pokusů)
   - Implementovat strategii postupného zlepšování dat při následných testech téže disku


## 4. Jiné certifikáty se generují v kartě disku a v historii

### Příčiny:
- Proces generování certifikátů není atomický - dochází ke stavu, kdy je částečně vytvořený certifikát uložen v různých místech
- Nedostatečná synchronizace mezi ukládáním do databáze a generováním souborového systému
- Chybí mechanismus pro detekci a řešení konfliktů při souběžném generování certifikátů
- Certifikát může být považován za „hotový“ ještě před úplným dokončením všech komponent (PDF, data, náhled)

### Řešení:
1. Implementovat transakční přístup k generování certifikátů:
   - obalit celé generování certifikátu do jedné transakce (databáze + souborový systém)
   - použít dvěfázový commit protokol pro zajištění konzistence
   - přidat mechanismus pro rollback při selhání kterékoli součásti

2. Zavést systém verzování a stavů certifikátů:
   - přidat pole CertificateStatus s hodnotami: Generating, Pending, Active, Archived, Failed
   - aktualizovat stav v průběhu generování aby bylo vidět, kde přesně proces uvízl
   - implementovat timeout mechanismus pro zabránění zaseknutí v nekonečném stavu

3. Přidat detekci a řešení duplicitních certifikátů:
   - před generováním nového certifikátu zkontrolovat existenci nedokončených verzí témže disku a relace
   - nabídnout možnost pokračovat, přepsat nebo sloučit s existující verzí
   - implementovat systém zamykání pro zabránění souběžného generování certifikátů témže disku

4. Vylepšit auditování a stopování generování certifikátů:
   - přidat podrobné logování každé fáze generování s časovými razítky
   - implementovat možnost zrušit probíhající generování certifikátu
   - přidat kontrolu integrity výsledného certifikátu před jeho uložením

