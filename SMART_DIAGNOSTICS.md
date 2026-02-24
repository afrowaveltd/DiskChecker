# DiskChecker - Windows SMART Diagnostics

Pokud program nevidí žádné disky, postupujte podle tohoto průvodce.

## ⚠️ Kritické - Spusťte jako SPRÁVCE

Program **MUSÍ** běžet s právy Administrátora aby mohl přistupovat k informacím o discích.

### Spuštění diagnostiky

```bash
# Spusťte z příkazové řádky administrátora (PowerShell)
cd D:\DiskChecker
DiskChecker.UI.exe --diagnostics
```

Nebo:

```bash
DiskChecker.UI.exe -d
```

## 🔧 Kontrola WMI služby

Otevřete PowerShell **jako Správce** a spusťte:

```powershell
# Zjistěte stav WMI
Get-Service WinRM

# Pokud není spuštěna, spusťte ji
Start-Service WinRM

# Restartujte WMI
Restart-Service WinRM
```

## 🖥️ Ručnítest - seznam disků

Spusťte v PowerShell **jako Správce**:

```powershell
# Seznamem fyzických disků
Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size | Format-Table

# Formátovanější výstup
Get-CimInstance Win32_DiskDrive | Format-Table DeviceID, Model, Size -AutoSize
```

**Očekávaný výstup:**
```
DeviceID           Model                  Size
--------           -----                  ----
\\.\PhysicalDrive0 Samsung SSD 970 EVO    1099511627776
\\.\PhysicalDrive1 WDC WD10EZEX-08M       1000204886016
```

## 🌡️ Kontrola SMART dat

V PowerShell **jako Správce**:

```powershell
# Zjistěte informace o fyzických discích
Get-PhysicalDisk | Format-Table DeviceId, Model, Size -AutoSize

# Získejte údaje o spolehlivosti
Get-PhysicalDisk | Get-StorageReliabilityCounter | Format-Table
```

## 📊 Instalace smartmontools (volitelně)

Pro pokročilejší SMART diagnostiku nainstalujte smartmontools:

```powershell
# Pomocí Windows Package Manager (doporučeno)
winget install smartmontools

# Nebo ručně stáhněte z: https://www.smartmontools.org/
```

Po instalaci testujte:

```powershell
# Skenování disků
smartctl --scan

# Čtení SMART dat z prvního disku
smartctl -a \\.\PhysicalDrive0
```

## 🔍 Běžné problémy

| Problém | Řešení |
|---------|--------|
| "Program neběží jako Správce" | Otevřete PowerShell / CMD jako Správce |
| "Žádné disky nenalezeny" | Zkontrolujte WMI servis (viz výše) |
| "Přístup odmítnut" | Povolte PowerShell skriptům běh: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser` |
| "SMART data se neloadují" | Nainstalujte smartmontools nebo zkontrolujte WMI |
| "Virtual Machine" | VM hypervisory nemusí vystavovat fyzické disky - kontaktujte správce |

## 📝 Logy a diagnostika

Program zapisuje diagnostické informace do konzole. Pokud máte problém:

1. Spusťte diagnostiku: `DiskChecker.UI.exe --diagnostics`
2. Zkopírujte si výstup
3. Ověřte, že:
   - ✓ Běžíte jako Správce
   - ✓ WMI služba je spuštěna
   - ✓ PowerShell povoluje spouštění skriptů
   - ✓ Systém má fyzické disky

## 🎯 Rychlý test

Spusťte v PowerShell **jako Správce**:

```powershell
# Zkopírujte a vložte:
$drives = Get-CimInstance Win32_DiskDrive
if ($drives) {
    Write-Host "✓ Disky nalezeny: $($drives.Count)"
    $drives | ForEach-Object { Write-Host "  - $($_.DeviceID): $($_.Model)" }
} else {
    Write-Host "✗ Žádné disky nebyly nalezeny!"
    Write-Host "  Ujistěte se, že:"
    Write-Host "  1. Běžíte jako Správce"
    Write-Host "  2. WMI služba je aktivní (Get-Service WinRM)"
}
```

---

Pokud problém přetrvává, kontaktujte podporu s výstupem diagnostiky.
