# Certifikáty DiskChecker – jednotný export (návrh)

## Proč

Původní certifikát používal společnou dlouhou rasterovou šablonu. U seek testu byly velké prázdné plochy, popisky výkonu zápisu byly uváděny i při čtecím režimu a graf měl jen 32 bodů. Zahrnoval také zvláštní druhou stránku pro hodnocení technika.

## Nové rozvržení

- Jediná stránka A4, stejné písmo, barvy, rámečky a hlavička pro různé druhy testu.
- Text přímo v PDF pomocí SkiaSharp (není to fotografie celé stránky), grafy jsou vektorově kreslené křivky.
- **QuickRead/FullRead**: primárně měřené čtení; zápis je N/A a nikdy není penalizován.
- **Seek**: latence v ms, P95/P99 a seek graf. Vyšší latence sama o sobě není automaticky důvod k vyřazení disku.
- **Sanitizace a destruktivní režimy**: pouze zde má smysl zvlášť uvádět měření zápisu/ověření dat.
- SMART shrnutí uvádí jen uložené údaje z certifikátu. Jiné nebo chybějící údaje se nedoplňují domněnkou.
- Samostatně jsou uvedeny komentáře testu, odborná interpretace technika a doporučení.
- Příliš dlouhé poznámky jsou na stránce **výslovně označeny jako zkrácené**. Úplný text zůstává dostupný v historii certifikátu.

## Společný certifikát

Na stránce Certifikát je u výběru test session tlačítko **Společné PDF (povrch + seek)**.

Program vezme zvolenou session, vyhledá nejbližší kompatibilní test druhého typu **na téže kartě disku** a připustí automatické spárování jen do **48 hodin**. Pokud pár nemá, nic nevymýšlí a informuje uživatele. Vybere dva zdrojové testy a vytvoří PDF s dvěma samostatnými panely.

**Známky se neprůměrují.** Oba testy mají samostatné skóre, chyby, poznámky, graf a technické posouzení. Společný PDF export nepřepisuje zdrojové certifikáty ani test session.

## Paměťová omezení

- Reprezentativní rychlostní body: max. **512 pro čtení** a **512 pro zápis** z SQL.
- Seek vzorky: max. **1024** z databáze při sestavování dvojice.
- Teploty: max. **256** bodů z databáze.
- Příliš velké historické seek JSON obálky (> 2 miliony znaků) se znovu nenačítají do plné objektové struktury. V takovém případě zobrazuje společný certifikát výslovně označené vzorkované odhady, pokud nejsou dostupné původní agregáty.
- V paměti je jediná A4 náhledová bitmapa; výstupní PDF tvoří vektorový dokument, ne raster stránky 1240×2400.

## Ověřování

Regresní testy zahrnují:

- úspěšný read-only test, nulové metriky zápisu, správnou známku;
- seek s nízkým skóre kvůli latenci, bez falešného závěru „porucha média“;
- oddělená skóre obou typů testů ve společném PDF;
- odmítnutí společného PDF pro dvě různé karty;
- vygenerovaný syntetický jednostránkový PDF jako GitHub Actions artefakt k vizuálnímu ověření.

Testy používají pouze syntetická data, nikdy fyzický disk.

## Známá omezení

- Automatická volba páru v rozmezí 48 hodin; ruční volba konkrétní druhé session může být další UX rozšíření.
- Jedna stránka nemůže obsahovat neomezený text všech komentářů; viditelné omezení je vědomý kompromis.
- Bez fyzického ověření vytištěného PDF a kontroly renderingu na obou platformách neoznačovat změnu za finálně validovanou.
