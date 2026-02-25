# Vylepšení terminálové aplikace - SMART Data Display

## Souhrn změn

Terminálová aplikace (DiskChecker.UI) byla vylepšena s přidáním živého zobrazení SMART dat během povrchových testů s využitím robustních funkcí Spectre.Console.

## Nové soubory

### 1. `DiskChecker.UI\Console\LiveSmartDisplay.cs`
**Nová třída pro živé zobrazení SMART dat**

Funkce:
- ✅ Načítání a zobrazování SMART dat v reálném čase
- ✅ Formátování do tabulky Spectre.Console
- ✅ Barevné označení kritických hodnot (teplota, chybné sektory)
- ✅ Kompaktní režim pro inline zobrazení
- ✅ Automatická aktualizace dat během testů

Klíčové metody:
- `StartMonitoringAsync()` - Načte počáteční SMART data
- `RefreshDataAsync()` - Aktualizuje data (voláno každých 10s během testu)
- `CreateSmartDataTable()` - Vytvoří formátovanou tabulku se SMART daty
- `CreateCompactStatus()` - Vytvoří kompaktní jednořádkový status

### 2. `DiskChecker.Tests\LiveSmartDisplayTests.cs`
**Unit testy pro novou funkcionalitu**

Pokryté scénáře:
- ✅ Inicializace a načtení dat
- ✅ Vytvoření SMART tabulky
- ✅ Generování kompaktního statusu
- ✅ Aktualizace dat (simulace periodického refreshe)

## Upravené soubory

### `DiskChecker.UI\Console\MainConsoleMenu.cs`

#### 1. Vylepšené SMART Check Menu (`CheckDiskMenuAsync`)

**Před:**
- Jednoduchá tabulka s údaji
- Základní textové výpisy

**Po:**
- 📊 **Tři skupiny tabulek:**
  - Informace o disku (model, kapacita)
  - Technické parametry (sériové číslo, firmware, teplota, hodiny)
  - Stav disku (sektorové chyby, hodnocení)
- ⏳ **Spinner** při načítání dat
- 🎯 **Barevné zvýraznění** kritických hodnot
- ✅ **ASCII separátory** pro verdikty (úspěch/selhání/varování)
- 📋 **Minimální table border** pro lepší kompatibilitu s terminálem

#### 2. Vylepšený Surface Test (`FullTestMenuAsync`)

**Před:**
- Dvouřádková tabulka s průběhem
- Žádné SMART info během testu

**Po:**
- 🔄 **Živá aktualizace:**
  - SMART data se automaticky aktualizují každých 10 sekund
  - Teplota disku v reálném čase
  - Okamžitá detekce nových chyb
- 📊 **Progress tabulka:**
  - Fáze 1: Zápis (rychlost, data, progress bar, chyby, ETA)
  - Fáze 2: Ověření (rychlost, data, progress bar, chyby, ETA)
  - Barevně odlišené fáze (cyan/yellow)
  - Jednoduchý text progress bar (████░░░░)
- ⏱️ **ETA kalkulace** pro obě fáze
- 📊 **SMART data na konci** - zobrazení finálního stavu disku
- 🎯 **Přesná detekce chyb** s barevným označením

## Technická implementace

### Aktualizační mechanismus

```csharp
// Inicializace SMART monitoringu
var smartDisplay = new LiveSmartDisplay(_smartCheckService);
await smartDisplay.StartMonitoringAsync(drive);

// V progress callbacku - aktualizace každých 10s
if ((DateTime.UtcNow - lastSmartUpdate).TotalSeconds >= 10)
{
    await smartDisplay.RefreshDataAsync(drive);
    lastSmartUpdate = DateTime.UtcNow;
}

// Aktualizace celého displaye
ctx.UpdateTarget(CreateProgressTable(/* metriky */));
```

### Spectre.Console funkce

Použité komponenty:
- ✅ **Table** s TableBorder.Minimal - Formátované tabulky bez komplikovaných rámů
- ✅ **Spinner/Status** - Animované načítání
- ✅ **Markup** - Barevný a stylizovaný text
- ✅ **Live Display** - Živá aktualizace obsahu

**Proč Minimal border:** Zajišťuje kompatibilitu se všemi terminály, včetně těch s omezenou podporou Unicode.

## Výsledný vzhled

### SMART Check

```
===== INFORMACE O DISKU =====
Parametr | Hodnota
---------|--------------------
Model    | Samsung SSD 970 EVO
Kapacita | 500.0 GB

===== TECHNICKÉ PARAMETRY =====
Parametr           | Hodnota
-------------------|----------------------------
Sériové číslo      | S466NX0N123456
Firmware           | 2B2QEXE7
Provozní hodiny    | 10000 h (1 rok 1 měsíc)
Teplota            | 35.0°C

===== STAV DISKU =====
Parametr                | Hodnota
------------------------|----------
Přemístěné sektory     | 0
Čekající sektory       | 0
Neopravitelné chyby    | 0

===== HODNOCENÍ KVALITY =====
  Známka: A
  Skóre:  95.0 / 100

-----------------------------------
  DISK JE V DOBRÉM STAVU
-----------------------------------
```

### Surface Test (během běhu)

```
Fáze      | Rychlost  | Data   | Průběh           | Chyby | Zbývá
----------|-----------|--------|------------------|-------|----------
Zápis     | 125.3 MB/s| 50.2 GB| ████████░░░░░░░░ |   0   | 2h 15m
Ověření   |  --       | 0 B    | ░░░░░░░░░░░░░░░░ |   0   | --:--:--

[Pokud jsou SMART data dostupná:]

===== STAV DISKU NA KONCI TESTU =====
Parametr           | Hodnota
-------------------|----------
Model disku        | Samsung SSD
Teplota (živě)     | 42.5 °C
Odpracováno        | 5000 h (5 měsíců)
Opotřebení SSD     | 15 %
Přemístěné sektory | 0
Čekající sektory   | 0
Neopravitelné chyby| 0
```

## Výhody oproti původní verzi

1. ✅ **Živé monitorování disku** - Vidíte změny teploty během testu
2. ✅ **Přehledná UI** - Jednotlivé tabulky pro různé informace
3. ✅ **Kompatibilita** - Funguje na všech terminálu, včetně těch s omezenou Unicode podporou
4. ✅ **Včasná detekce problémů** - Rostoucí teplota nebo chyby jsou ihned viditelné
5. ✅ **Profesionální vzhled** - Čistý, srozumitelný formát
6. ✅ **Robustnost** - Bez komplikovaných UI komponent, které se nekreslí správně

## Kompatibilita

- ✅ **Windows PowerShell** - Plná podpora
- ✅ **Windows Terminal** - Doporučeno
- ✅ **Linux/Mac terminál** - Plná podpora
- ✅ **Omezené terminály** - Fallback na ASCII, bez komplikovaných rámců

## Testování

Všechny nové funkce jsou pokryty unit testy v `LiveSmartDisplayTests.cs`.

Build je úspěšný ✅

## Budoucí vylepšení

Možnosti dalšího rozšíření:
- [ ] Konfigurovatelná frekvence aktualizace SMART dat
- [ ] Zvuková notifikace při kritických hodnotách
- [ ] Export logu během testu
- [ ] Více detailů v SMART tabulce (např. power-on time v dnech/měsících)
