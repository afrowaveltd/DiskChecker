# 🔧 DiskChecker - Pokyny k řešení problémů s detekcí disků (Windows)

Právě jsem **OPRAVIL** problém s detekcí disků ve Windows verzi DiskChecker!

## ✅ Co bylo opraveno

1. **PowerShell skript escaping** - Nové PowerShell skripty nyní používají temporární soubory místo inline příkazů, což eliminuje problémy s escapingem složitých znaků.

2. **Robustní device matching** - Zlepšila se detekce disků z cest jako `\\.\PhysicalDrive0`.

3. **Diagnostika** - Přidána nová diagnostická aplikace pro snadnou identifikaci problémů.

## 🚀 Jak testovat

### Spuštění diagnostiky (NEJDŮLEŽITĚJŠÍ)

```bash
# Spusťte PowerShell JAKO SPRÁVCE (oba příkazy dělají totéž)
D:\DiskChecker\DiskChecker.UI.exe --diagnostics
# nebo
D:\DiskChecker\DiskChecker.UI.exe -d
```

Diagnostika vám zobrazí:
- ✓ Operační systém
- ✓ Počet nalezených disků
- ✓ Detail každého disku (model, cesta, velikost)
- ✓ Kontrola závislostí (smartmontools)
- ✓ Test čtení SMART dat z prvního disku

### Ruční ověření v PowerShell (Admin)

```powershell
# Seznamem fyzických disků
Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size | ConvertTo-Json

# Nebo jednodušeji:
Get-CimInstance Win32_DiskDrive | Format-Table DeviceID, Model, Size
```

**Očekávaný výstup:**
```
DeviceID           Model                  Size
--------           -----                  ----
\\.\PhysicalDrive0 Samsung 970 EVO    1099511627776
```

## ⚠️ KRITICKÉ - SPUŠTĚNÍ JAKO SPRÁVCE

Program **MUSÍ** běžet s právy Administrátora! 

Pokud neběží jako Správce:
1. Otevřete PowerShell kliknutím pravého tlačítka
2. Vyberte "Spustit jako správce"
3. Navigujte do DiskChecker složky
4. Spusťte: `.\DiskChecker.UI.exe`

## 🔍 Pokud pořád nevidíte disky

Postupujte podle průvodce v **`SMART_DIAGNOSTICS.md`** - tam najdete:
- Kontrolu WMI služby
- PowerShell execution policy nastavení
- Instalaci smartmontools
- Běžné problémy a řešení

## 📝 Soubory, které jsem připravil

- **`SMART_DIAGNOSTICS.md`** - Detailní troubleshooting průvodce
- **`DiagnosticsApp.cs`** - Nová diagnostická aplikace
- **Aktualizovaný `WindowsSmartaProvider.cs`** - Opravená PowerShell integrace
- **Testy** pro ověření funkcionalit

## 🎯 Příští kroky

1. Spusťte: `DiskChecker.UI.exe --diagnostics` (jako Správce)
2. Zkontrolujte, zda vidíte své disky
3. Pokud vidíte své disky → program je opravený! ✅
4. Pokud stále nic → podívejte se do `SMART_DIAGNOSTICS.md`

## 📞 Debugování (pro vývojáře)

Pokud chcete vidět detailnější logy:

```csharp
// V Program.cs (UI projektu):
services.AddLogging(logging => 
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug); // ← Změňte na Debug
});
```

---

**Prosím spusťte diagnostiku a dejte mi vědět jaký je výsledek!** 🙏
