# DiskChecker

Profesionální nástroj pro kontrolu a testování úložných zařízení s konzolovým i webovým rozhraním.

## 🚀 Vlastnosti

- **SMART Data** - čtení technických parametrů disků (Windows i Linux)
- **Kvalita disku** - automatické vyhodnocení A-F podle SMART atributů
- **Certifikáty** - generování textových dokumentů s detailními výsledky
- **Historie** - ukládání výsledků do SQLite databáze  
- **Porovnávání** - srovnání více disků nebo opakovaných testů
- **Bilingual** - čeština a angličtin s možností přepínání

## 📋 Podporovaná rozhraní

- **Console UI** - pro TUI prostředí (Windows/Linux)
- **Web UI** - Blazor Server + Kestrel pro přístup přes prohlížeč
- **Avalonia UI** - WPF-like desktopová aplikace (připraveno)

## 📋 Požadavky

- .NET 10 Runtime (nebo SDK pro vývoj)
- Windows 10+ nebo Linux (Debian/Ubuntu s `smartctl`)
- Práva pro čtení SMART dat (v Linuxu potřeba `smartmontools`)

```bash
# Linux installs
sudo apt-get install smartmontools
```

## 🛠 Instalace a použití

```bash
git clone https://github.com/yourusername/DiskChecker.git
cd DiskChecker
dotnet build -c Release
```

### Console UI (TUI)
```bash
dotnet run --project DiskChecker.UI
```

### Web UI (Blazor Server)
```bash
dotnet run --project DiskChecker.Web
```

Přejděte na `https://localhost:5001` nebo `http://localhost:5000`

## 🏗 Architektura

```
DiskChecker/
├── Core/           # Modely, rozhraní, business logika
├── Application/    # DTO, služby
├── Infrastructure/ # EF Core, SMART čtečky, databáze
├── UI/             # Konzolové rozhraní (Spectre.Console)
├── Web/            # Blazor Server + Kestrel web UI
└── Tests/          # Unit testy (xUnit + NSubstitute)
```

## 🌐 Lokalizace

Podporuje češtinu a angličtinu. Všechny uživatelské texty jsou lokalizovatelné.

## 📄 Certifikáty

Certifikáty jsou generovány v textové formě pro tisk a archivaci. Obsahují:
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

**DiskChecker** - vaše disková bezpečnost v terminálu i přes síť.
