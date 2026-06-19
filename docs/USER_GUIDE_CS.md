# DiskChecker – Uživatelská příručka

Profesionální nástroj pro diagnostiku, testování a certifikaci pevných disků (HDD/SSD/NVMe).

---

## Obsah

1. [Instalace](#instalace)
2. [Závislosti](#závislosti)
3. [První spuštění](#první-spuštění)
4. [Přehled funkcí](#přehled-funkcí)
5. [SMART kontrola](#smart-kontrola)
6. [Test povrchu](#test-povrchu)
7. [Seek test](#seek-test)
8. [Destruktivní test](#destruktivní-test)
9. [Bezpečný destruktivní test](#bezpečný-destruktivní-test)
10. [Záloha a obnova](#záloha-a-obnova)
11. [Certifikáty](#certifikáty)
12. [Historie a karty disků](#historie-a-karty-disků)
13. [Porovnání disků](#porovnání-disků)
14. [Řešení problémů](#řešení-problémů)

---

## Instalace

### Windows

1. **Stáhněte** nejnovější verzi z [GitHub Releases](https://github.com/afrowave/diskchecker/releases)
2. **Rozbalte** archiv `DiskChecker_win-x64.zip` do libovolné složky
3. **Spusťte** `DiskChecker.UI.Avalonia.exe` **jako Správce** (pravým tlačítkem → Spustit jako správce)

> ⚠️ **DŮLEŽITÉ**: Aplikace **musí** běžet s oprávněními správce/administrátora, protože potřebuje přímý přístup k fyzickým diskům (`\\.\PhysicalDriveX`).

### Linux

1. **Nainstalujte závislosti** (viz [Závislosti](#závislosti))
2. **Stáhněte** `DiskChecker_linux-x64.tar.gz`
3. **Rozbalte**: `tar -xzf DiskChecker_linux-x64.tar.gz`
4. **Spusťte s root právy**: `sudo ./DiskChecker.UI.Avalonia`

> ⚠️ **DŮLEŽITÉ**: Na Linuxu aplikace vyžaduje root práva pro přístup k `/dev/sdX` a `/dev/nvmeXn1`.

### Sestavení ze zdrojových kódů

```bash
# Požadavky: .NET 10.0 SDK
git clone https://github.com/afrowave/diskchecker.git
cd DiskChecker
dotnet restore
dotnet build --configuration Release
dotnet run --project DiskChecker.UI.Avalonia
```

---

## Závislosti

### Windows

| Závislost | Nutnost | Poznámka |
|-----------|---------|----------|
| .NET 10.0 Runtime | ✅ Povinné | Součást self-contained buildu |
| smartmontools (smartctl) | ⚠️ Doporučeno | Pro plnou SMART podporu. [Stáhnout](https://sourceforge.net/projects/smartmontools/) |
| PowerShell 5.1+ | ✅ Povinné | Součást Windows 10+ |

**Instalace smartmontools na Windows:**
1. Stáhněte instalační `.exe` z [smartmontools SourceForge](https://sourceforge.net/projects/smartmontools/files/smartmontools/)
2. Nainstalujte do výchozí cesty (`C:\Program Files\smartmontools\bin\smartctl.exe`)
3. Aplikace automaticky detekuje `smartctl.exe` na standardních cestách

### Linux

| Závislost | Nutnost | Instalace (Debian/Ubuntu) |
|-----------|---------|---------------------------|
| .NET 10.0 Runtime | ✅ Povinné | Součást self-contained buildu |
| smartmontools | ✅ Povinné | `sudo apt install smartmontools` |
| ntfs-3g | ⚠️ Doporučeno | `sudo apt install ntfs-3g` (pro čtení NTFS disků) |
| hdparm | ⚠️ Doporučeno | `sudo apt install hdparm` (pro detekci rozhraní) |

```bash
# Jednorázová instalace všech závislostí (Debian/Ubuntu)
sudo apt update
sudo apt install smartmontools ntfs-3g hdparm usbutils
```

---

## První spuštění

1. Spusťte aplikaci **s oprávněními správce/root**
2. Na hlavní obrazovce klikněte na **🔍 SMART Check**
3. Aplikace automaticky detekuje všechny připojené fyzické disky
4. U každého disku se zobrazí:
   - Model, sériové číslo, kapacita
   - Známka zdraví (A–F) – pokud je dostupné SMART
   - Typ rozhraní a rychlost připojení
   - Počet předchozích testů

---

## Přehled funkcí

| Funkce | Popis | Destruktivní? |
|--------|-------|---------------|
| 🔍 **SMART Check** | Analýza SMART atributů, známka zdraví, predikce selhání | ❌ Ne |
| 🧪 **Test povrchu** | Sekvenční zápis + čtení celého disku s grafem rychlosti | ⚠️ Volitelně |
| 🎯 **Seek Test** | Měření latence seek operací (Full Stroke / Náhodný / Skip) | ❌ Ne |
| 💣 **Destruktivní test** | Kompletní: 2× sanitizace + 3× seek testy + SMART delta | ✅ Ano |
| 🛡️ **Bezpečný destruktivní** | Záloha → Destruktivní test → Obnova dat | ✅ Ano (s ochranou) |
| 💾 **Záloha** | Záloha souborů nebo raw image disku | ❌ Ne |
| 🔄 **Obnova** | Obnova dat ze zálohy na nový disk | ✅ Ano (na cílový disk) |
| 🏅 **Certifikáty** | Prohlížeč všech vygenerovaných certifikátů | – |
| 📊 **Historie** | Historie testů, karty disků, trendy | – |

---

## SMART kontrola

### Co SMART ukazuje

- **Celkové zdraví**: PASSED / FAILED
- **Známka A–F**: Vypočtena z klíčových atributů (reallocated sektory, pending sektory, teplota, wear leveling...)
- **Predikce selhání**: Pokud SMART hlásí `FAILING_NOW`, zobrazí se červený varovný banner
- **Teplota**: Aktuální teplota disku
- **Power-On Hours**: Celkový čas provozu
- **Rychlost připojení**: Detekováno rozhraní (SATA 6Gb/s, USB 3.x, NVMe PCIe Gen4...)

### Varovný banner

Pokud disk vykazuje známky blížícího se selhání:
- 🔴 Červený banner s podrobnostmi
- Tlačítko **💾 Zálohovat data** – přejde do zálohovacího modulu
- Tlačítko **🔍 Přesto testovat** – umožní pokračovat i přes varování

### Self-testy

- **Krátký test** (~2 minuty) – rychlá kontrola elektromechanických částí
- **Dlouhý test** (může trvat hodiny) – kompletní sken povrchu
- Průběh testu se zobrazuje v procentech (0–100%)

---

## Test povrchu

### Režimy

| Režim | Popis | Rychlost |
|-------|-------|----------|
| **Jen čtení** | Čte celý disk, nemění data | Rychlý |
| **Zápis + čtení** | Zapíše testovací vzor a ověří ho | Střední |
| **Zero-fill** | Zapíše nuly a ověří – sanitizace | Pomalý |

### Co test ukazuje

- **Graf rychlosti** v reálném čase (osa X: pozice na disku, osa Y: MB/s)
- **Aktuální rychlost**, průměr, minimum, maximum
- **Teplota** během testu
- **Chyby**: počet chyb čtení/zápisu
- **Progress bar** s procenty a odhadem času dokončení

### Výsledek

- Známka A–F podle konzistence rychlosti a počtu chyb
- Uložení do historie disku
- Vygenerování certifikátu (PDF)

---

## Seek test

### Typy testů

| Typ | Popis |
|-----|-------|
| **Full Stroke** | Seeky přes celý rozsah disku (0% → 100% → 0%) |
| **Náhodný** | Seeky na náhodné pozice |
| **Přeskakování** | Seeky s pevným krokem (např. každých 10% kapacity) |

### Co test ukazuje

- **Graf latence** v reálném čase (osa X: seek #, osa Y: latence v ms)
- **Statistiky**: průměr, medián, P95, P99, minimum, maximum
- **Barevné odlišení**: zelená (dobré), oranžová (varování), červená (podezřelé)
- **Tabulka vzorků** s detaily každého seeku

### Doporučení

Aplikace automaticky analyzuje SMART data a doporučí:
- Počet seeků (100–5000)
- Typ testu
- Zda použít pre-positioning (pro HDD)

---

## Destruktivní test

> ⚠️ **VAROVÁNÍ**: Tento test **nenávratně zničí všechna data** na disku!

### Fáze testu

| Fáze | Popis |
|------|-------|
| **0. Příprava** | SMART snímek, detekce SSD/HDD |
| **1. Sanitizace #1** | Zápis nul + ověření čtením |
| **2. Seek Full Stroke** | Měření latence přes celý disk |
| **3. Seek Náhodný** | Měření latence na náhodných pozicích |
| **4. Seek Přeskakování** | Měření latence s pevným krokem |
| **5. Sanitizace #2** | Druhý průchod zápisu nul + ověření |
| **6. Finalizace** | SMART delta, certifikát, statistiky |

### Grafy

- **Sanitizační graf**: 4 série (1. zápis, 1. čtení, 2. zápis, 2. čtení) – každá jinou barvou
- **Seek grafy**: Přepínatelné mezi Full Stroke / Náhodný / Skip
- **Legenda s toggle**: Kliknutím na legendu lze série vypínat/zapínat

### Po dokončení

- **📊 Statistika**: Průměry, teplota, chyby, zmizení disku
- **🔬 SMART změny**: Delta atributů (teplota, reallocated, pending...)
- **🏅 Certifikát**: Uložen do DB a PDF

### Recovery

Pokud disk během testu zmizí:
- Oranžový panel s odpočtem (10 minut)
- Automatická detekce návratu disku
- Re-inicializace a pokračování testu
- Záznam všech výpadků do logu

---

## Bezpečný destruktivní test

Kombinuje zálohu, destruktivní test a obnovu do jednoho workflow:

1. **💾 Fáze 1 – Záloha**: Vytvoří kompletní bitovou kopii disku (raw image)
2. **💣 Fáze 2 – Destruktivní test**: Provede plný destruktivní test
3. **🔄 Fáze 3 – Obnova**: Obnoví data ze zálohy zpět na disk

> ✅ **Výhoda**: Data jsou chráněna – po testu je disk ve stejném stavu jako před ním.

---

## Záloha a obnova

### Režimy zálohy

| Režim | Popis | Použití |
|-------|-------|---------|
| **Souborová** | Kopíruje vybrané složky | Když je FS čitelný (NTFS, EXT4...) |
| **Raw image** | Bitová kopie celého disku | BitLocker, neznámý FS, poškozený FS |

### Výběr složek

- **👤 Uživatelské** – Users, Desktop, Documents, Downloads, Pictures, Music, Videos
- **Vše** – Označí všechny nalezené složky
- **Nic** – Zruší výběr
- **Multi-disk**: Data lze rozdělit mezi více cílových disků

### Kalkulace místa

- Zobrazuje potřebné vs. dostupné místo
- **Systémová rezerva**: Automaticky chrání volné místo pro OS (dle velikosti RAM)
- Varování při nedostatku místa

### Obnova

- Automaticky skenuje všechny disky a hledá složky se zálohami
- Parsuje `backup_manifest.json` – zobrazí metadata zálohy
- **Verifikace**: Po obnově zkontroluje existenci a velikost všech souborů

---

## Certifikáty

### Prohlížeč certifikátů

- **Levý panel**: DataGrid se všemi certifikáty (filtrování dle známky, fulltext)
- **Pravý panel**: Kompletní rekonstrukce certifikátu:
  - Info o disku (model, sériové číslo, kapacita, rozhraní)
  - Známka v pečeti (A–F)
  - Výsledky testu
  - Graf výkonu (Canvas polyline)
  - SMART souhrn
  - Seek metriky
  - Porovnání sanitizací
  - SMART delta
  - Diagnostické signály
  - Důvody hodnocení
  - Doporučení
  - Poznámky

### Export

- **PDF**: Každý certifikát lze exportovat do PDF
- **Tisk**: Přímý tisk certifikátu
- **Email**: Odeslání certifikátu emailem (je-li nakonfigurováno SMTP)

---

## Historie a karty disků

### Karta disku

Každý testovaný disk má vlastní kartu s:
- Základními informacemi (model, sériové číslo, kapacita, rozhraní)
- Historií všech testů
- Trendem známek
- SMART historií
- Certifikáty

### Záložky detailu karty

- **📋 Přehled**: Souhrn všech metrik
- **📊 Testy**: DataGrid se všemi testy disku
- **🏅 Certifikáty**: Všechny certifikáty disku
- **🔬 SMART**: Historie SMART atributů

---

## Porovnání disků

- Vyberte 2–5 disků pro porovnání
- Grafy: rychlost, latence, teplota
- Tabulka: všechny metriky vedle sebe
- Zvýraznění vítěze v každé kategorii

---

## Řešení problémů

### SMART data nedostupná

**Příznak**: "SMART data nedostupná – disk nemusí podporovat SMART (např. USB adaptér)"

**Příčiny**:
- Disk je připojen přes USB adaptér, který nepodporuje SMART passthrough
- `smartctl` není nainstalován (Linux) nebo není na PATH (Windows)
- Disk je virtuální (VHD, iSCSI)

**Řešení**:
1. Na Linuxu: `sudo apt install smartmontools`
2. Na Windows: Stáhněte a nainstalujte [smartmontools](https://sourceforge.net/projects/smartmontools/)
3. Zkuste jiný USB adaptér (některé čipy jako JMicron/ASMedia SMART podporují)
4. Aplikace automaticky zkouší 3 režimy: `auto`, `sat`, `usbprolific`

### Aplikace nevidí disky

- **Windows**: Spusťte jako Správce
- **Linux**: Spusťte s `sudo`
- Zkontrolujte, že disk není připojen jako dynamický disk ve Windows

### Test je pomalý

- **USB 2.0**: Maximální rychlost ~40 MB/s – test velkého disku může trvat hodiny
- **Starý HDD**: Rychlost může klesat ke konci disku (normální chování)
- **Antivirus**: Dočasně vypněte real-time ochranu pro testovaný disk

### Disk zmizel během testu

- Aplikace automaticky spustí 10minutový recovery režim
- Zkontrolujte fyzické připojení (kabel, napájení)
- Pokud se disk vrátí, test bude pokračovat
- Pokud ne, test se ukončí s dokumentací výpadku

### Graf se nezobrazuje

- SeekTest: První test může potřebovat chvíli na inicializaci grafu
- Přepínání grafů: Klikněte na legendu pro zapnutí/vypnutí sérií
- Windows: Ujistěte se, že máte nainstalované VC++ Redistributable (pro SkiaSharp)

---

## Klávesové zkratky

| Zkratka | Akce |
|---------|------|
| `Esc` | Zpět / Zavřít |
| `F5` | Obnovit |
| `Ctrl+Enter` | Spustit test |

---

## Podpora a hlášení chyb

- **GitHub Issues**: [github.com/afrowave/diskchecker/issues](https://github.com/afrowave/diskchecker/issues)
- **Wiki**: [github.com/afrowave/diskchecker/wiki](https://github.com/afrowave/diskchecker/wiki)

---

*DiskChecker – Poctivý open-source nástroj pro diagnostiku disků. Vytvořeno s ❤️ pro IT komunitu.*
