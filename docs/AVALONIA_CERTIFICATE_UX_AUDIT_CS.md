# DiskChecker Avalonia: audit návazností a certifikátů

Stav první etapy, 21. listopadu 2026. Tato revize mapuje celý tok výběru disku → test → certifikát, **nikoli** úplné funkční ověření všech podstránek a hardwarových operací.

## Pravidla (závazná pro příslušné obrazovky)

1. Identita disku se v historii řídí \`DiskCard.Id\`, ne znovu použitelnou cestou \`/dev/sdX\` nebo písmenem Windows.
2. Konkrétní výsledky identifikuje \`TestSession.Id\`; \`DiskCertificate.Id\` je samostatný historický výstup, vždy navázaný na odpovídající \`TestSessionId\`.
3. Uživatelský výběr testu má přednost před „nejnovějším certifikátem“. Automaticky vybraný pár pro společné PDF je pouze návrh, nikoli povinná kombinace.
4. Bez dat nesmí být na grafu uměle vykreslený průběh ani rychlost zápisu u read-only/seek testu.
5. Graf z SQL používá **naměřené** procento průběhu; archivní certifikát bez původních souřadnic je označitelný jen jako historický vzorkovaný profil. Nulová rychlost může signalizovat stall — neschovávat ji průměrováním.
6. Pomalá latence není automaticky prokázaná fyzická porucha média; rozhoduje typ testu, SMART a skutečné I/O chyby.
7. Souběžné načítání výsledků nesmí přepsat novější výběr. Při změně fyzického disku zahodit staré identifikátory.

## Kdo má co zobrazovat

| Obrazovka | Jednoznačná odpovědnost | Stav této etapy |
| --- | --- | --- |
| **Výběr disku** (\`DiskSelection\`) | Výběr živého zařízení; jeho aktuální stav | Přezkoumán kontext navigace; nikoli vlastní detekce zařízení |
| **Karty disků** (\`DiskCards\`) | Inventář disků, agregovaný stav a přechod na jejich historii | Opravena vazba nejnovější session ↔ její certifikát |
| **Detail karty** (\`DiskCardDetail\`) | Timeline **všech** testů jednoho disku, SMART a poznámky, výběr konkrétního testu, volitelně OxyPlot analytické grafy | Opraven přenos volby a pozůstatek testu jiného disku |
| **Historie** (\`History\`) | Přehled historických záznamů (legacy history service) | Typ + čas při mapování na session, namísto samotného času |
| **Reporty** (\`Report\`) | Seznam jednotlivých **test sessions** včetně testů bez PDF, JSON report a export | Přímý přechod na vybraný test a společné PDF |
| **Prohlížeč certifikátů** (\`CertificateBrowser\`) | Index **již vystavených** dokumentů, filtry, jejich grafy a autorizovaný komentář | Opraven read-only graf, výběr a přímé otevření detailu/konkrétní karty |
| **Certifikát** (\`CertificateView\`) | Podrobnosti **vybraného** testu; uložený certifikát a jeho komentáře; jednotný tisk / export i sdružené PDF | Sjednoceny grafy, výběr z historie a možnost ruční volby druhého testu |
| **Analýza** (\`Analysis\`) | Detail anomálií, stallů, SMART trendů a zoom | Nezaměňovat s certifikátem; hlubší kontrola analytických škál zůstává mimo tuto etapu |
| **Porovnání disků** (\`DiskComparison\`) | Hodnocení více fyzických disků; nikoli zdroj primárních certifikovaných metrik | Zatím jen kontrola role v navigaci |
| **Plný report** (\`FullReportViewer\`) | Zobrazení a otevření vygenerovaného JSON/reportového dokumentu | Oddělit od certifikátu; detailní audit importu a tisku v další etapě |
| **Surface/Seek/SMART test** | Provedení vlastního testu a uložení skutečných naměřených výsledků | Nezasahovat do I/O pouze kvůli změně náhledu |
| **Absolute/Safe Destructive** | Přesně vymezené destruktivní operace a recovery | Mimo rozsah UI opravy; vyžadují separátní bezpečnostní revizi |
| **Backup/Restore** | Zálohování a obnovování; nezávislé na diagnostice | Bez změn |
| **Export/Import** | Přenos dat mezi instalacemi | Bez změn |
| **Settings** | Konfigurace služby a jazyka | Pouze nové překlady příslušných akcí |

### Záměrně odlišné grafy

Certifikát zobrazuje stručný profil naměřeného výkonu. Analýza může zobrazovat nezávislý OxyPlot/LiveCharts průběh s anomáliemi a přiblížením, protože má jiný účel. **Hodnoty a jejich význam ale musejí zůstat shodné**. Neslučovat do jednoho grafu SMART teplotu v °C a MB/s bez samostatné vysvětlené škály.

### Co je opraveno v tomto PR

- \`CertificateChartProjector\`: jediný postup pro převod vzorků na polylines v obou certifikačních pohledech.
- No-data znamená prázdný graf, nikoli vložené demo křivky ani rekonstruovaný „vrchol“ z průměru/maxima.
- Zachovává nulové propady a čas/procento původního měření, používá konečné bounded collections.
- Jednostranné profily (pouze read) jsou platné. Seek latency se zobrazuje zvlášť.
- Opravena navigace z karet, jejich detailu, historie, reportů a prohlížeče.
- Ruční párování seek + povrchový test s možností starších výsledků; automatic default omezen na 48h.
- Normalizace poměru stran on-screen grafů a ochrana proti neaktuálním async výsledkům.
- Prohlížeč filtruje také typ testu, známku a datum, udržuje viditelný výběr synchronní s detailem.
- PDF export z browseru zachovává uloženou historii rychlostí včetně stallů.

## Další ověřovací úkoly (zatím NEJSOU hotové)

1. Vizuálně otevřít **všechny** relevantní karty na Debianu i Windows a potvrdit světlý/tmavý režim, resize, tab order, tooltipy a klávesové ovládání.
2. Zkontrolovat seek/scatter plot v \`DiskCardDetail\`, který stále používá vlastní OxyPlot renderer a někdy deserializuje celý seek JSON.
3. Zrevidovat \`Analysis\` vs \`Report\` grafické osy a zobrazované jednotky, obzvlášť teplotu.
4. Zkontrolovat data a navazování historických \`HistoricalTest\`, pro které již odpovídající \`TestSession\` neexistuje.
5. Udělat integrační průchod s reálnou testovací SQLite: prohození disků se stejnou OS cestou, read-only, seek, sanitizace, kombinovaný export, restart programu a otevření starého PDF.
6. Externí autorizované komentáře v PDF a chování importovaných, archivovaných nebo smazaných karet.
7. Samostatná bezpečnostní revize backup/restore/destruktivních režimů, **nikoli** automatické spojování do certifikačního refaktoringu.

Tento soubor je pracovní audit první dokončované etapy. Zelené unit testy samy o sobě neprokazují správný vzhled ani bezpečnost skutečných I/O operací.
