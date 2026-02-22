# DiskChecker

Profesionální nástroj pro kontrolu a testování úložných zařízení v terminálovém prostředí.

## 🚀 Vlastnosti

- **SMART Data** - čtení technických parametrů disků (Windows i Linux)
- **Kvalita disku** - automatické vyhodnocení A-F podle SMART atributů
- **Certifikáty** - generování textových dokumentů s detailními výsledky
- **Historie** - ukládání výsledků do SQLite databáze  
- **Porovnávání** - srovnání více disků nebo opakovaných testů
- **Bilingual** - čeština a angličtina s možností přepínání

## 📋 Požadavky

- .NET 10 Runtime (nebo SDK pro vývoj)
- Windows 10+ nebo Linux (Debian/Ubuntu s `smartctl`)
- Práva pro čtení SMART dat (v Linuxu potřeba `smartmontools`)

```bash
# Linux installs
sudo apt-get install smartmontools
```

## 🛠 Instalace

```bash
git clone https://github.com/yourusername/DiskChecker.git
cd DiskChecker
dotnet build -c Release
dotnet run --project DiskChecker.UI
```

## 📖 Použití

```
DiskChecker - hlavní menu
1. Kontrola disku (SMART) - získá SMART data a vyhodnotí kvalitu
2. Úplný test (zápis/nula + kontrola) - prošpehování disku
3. Historie testů - zobrazí dřívější testy
4. Porovnání disků - srovnání více disků
5. Nastavení (jazyk) - přepínání jazyka
```

## 🏗 Architektura

```
DiskChecker/
├── Core/           # Modely, rozhraní, business logika
├── Application/    # DTO, služby
├── Infrastructure/ # EF Core, SMART čtečky, databáze
└── UI/             # Konzolové rozhraní (Spectre.Console)
```

## 🌐 Lokalizace

Podporuje češtinu a angličtinu. V nastavení lze jazyk změnit.  
Všechny uživatelské texty jsou lokalizovatelné.

## 📄 Certifikáty

Certifikáty jsou generovány v textové formě pro tisk a archivaci.  
Obsahují:
- Identifikační údaje disku
- SMART parametry
- Kvalitní hodnocení (A-F)
- Upozornění a doporučení
- Časový stempel

## 📝 Licence

MIT - svobodný open-source software

## 👨‍💻 Vývojáři

- Vývoj v češtině pro české i mezinárodní komunitu

## 🤝 Přispívání

Příspěvky Jsou VÍTANÉ! Prosím otevřete issue nebo pull request.

---

**DiskChecker** - vaše disková bezpečnost v terminálu.
