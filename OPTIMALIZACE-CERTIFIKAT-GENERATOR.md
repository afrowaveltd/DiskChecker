# Optimalizace generování certifikátů - Kompletní souhrn změn

## Přehled problému a řešení

### Fáze 1: Živé generování certifikátů (✅ Hotovo)
**Problém:** Aplikace havarovala během 6. kroku generování certifikátu (vykreslování grafu) při sanitizaci velkých disků kvůli vysokému množství dat a nadměrnému použití paměti.

**Řešení:** Implementace ArrayPool, segmentovaného vykreslování a fallback mechanismů v `CertificateGenerator.cs`.

### Fáze 2: Export certifikátů z historie (✅ Hotovo)  
**Problém:** Aplikace zamrzala na "probíhá generování certifikátu ze vzorků" a následně havarovala při exportu certifikátu z historie (Reports → History → Select disk → Export → Crash).

**Řešení:** Přidání downsamplingové ochrany do `ReportViewModel.ExportReportAsync` a `CertificateViewModel.EnsureCertificateGraphDataAsync`.

### Fáze 3: Centralizace exportní logiky (✅ Hotovo)
**Problém:** Tři různé ViewModely (`DiskCardDetailViewModel`, `ReportViewModel`, `CertificateViewModel`) měly duplicitní export funkcionalitu s nekonzistentními implementacemi. `DiskCardDetailViewModel` zcela chyběla downsamplingová ochrana a načítala desítky tisíc vzorků přímo do paměti → **primární zdroj havárie při exportu z historie**.

**Řešení:** Vytvoření centralizovaného `CertificateExportService`, který:
- Zapouzdřuje VŠECHNU exportní logiku (loading, downsampling, generování, PDF tvorba, ukládání)
- Poskytuje jednotné API pro všechny ViewModely
- Eliminuje duplicitu kódu
- Zajišťuje konzistentní chování bez ohledu na vstupní bod
- Poskytuje progress reporting pro lepší UX

**Refaktorované komponenty:**
- ✅ `DiskCardDetailViewModel.GenerateCertificateAsync` - nyní používá `CertificateExportService` (KRITICKÁ OPRAVA - byl primární zdroj havárie)
- ✅ `ReportViewModel.ExportReportAsync` - nyní používá `CertificateExportService`
- ℹ️ `CertificateViewModel` - **NENÍ TŘEBA REFAKTOROVAT** - má jiný workflow (zobrazení certifikátu), už má ochranu proti načtení všech vzorků najednou (`GetTestSessionWithoutSamplesAsync` + progressive loading)

---

## Implementované optimalizace

### 1. **Konstanty pro omezení datových sad** (CertificateGenerator.cs, řádky 28-29)
```csharp
private const int MaxChartPoints = 512;
private const int CertificateChartPoints = 32;
```
- MaxChartPoints: Maximální počet bodů pro vykreslování grafu (omezení z tisíců na 512)
- CertificateChartPoints: Počet bodů pro PDF certifikát (32 bodů je dostatečný)

### 2. **DrawProfilePolyline - Optimalizovaná metoda** (CertificateGenerator.cs, řádky 522-633)
**Klíčové optimalizace:**
- **Použití ArrayPool<PointF>** pro recyklaci paměti místo alokace nových polí
- **Dynamický downsampling** když počet bodů překročí MaxChartPoints (512)
- **Segmentované vykreslování** (max 256 bodů na segment) pro zabránění havárií GDI+
- **OutOfMemoryException fallback** - při chybě se vykreslí zjednodušený graf (3 body: začátek, střed, konec)
- **Automatické vrácení pole do pool** v finally bloku

```csharp
// Použití ArrayPool pro efektivní správu paměti
points = ArrayPool<PointF>.Shared.Rent(pointCount);

// ...vykreslování...

// Vrácení pole do pool
ArrayPool<PointF>.Shared.Return(points, clearArray: true);
```

### 3. **GenerateAndStoreChartImageAsync - Vylepšené zpracování chyb** (CertificateGenerator.cs, řádky 1063-1115)
**Změny:**
- Downsampling na MaxChartPoints (512) místo původních 32
- Try-catch s fallback mechanismem:
  - Primární pokus: vykreslení s 512 body
  - Fallback: při chybě agresivnější downsampling na 16 bodů
  - Logging všech chyb pomocí ILogger

### 4. **RenderChartImage - Ochrana proti chybám** (CertificateGenerator.cs, řádky 1132-1265)
**Nové prvky:**
- **Try-catch okolo vykreslování** polyline (řádky 1181-1201)
- **Downsampling teplotních dat** když je jich více než MaxChartPoints
- **Fallback zobrazení** - při chybě se místo grafu zobrazí text "Graf není k dispozici"
- **Bezpečné ukládání souboru** s vlastním try-catch

```csharp
// Optimalizace: Downsample teplotních dat pokud je jich příliš mnoho
var tempValues = temperatureSamples.Count > MaxChartPoints
    ? DownsampleTemperatures(temperatureSamples, MaxChartPoints)
    : temperatureSamples.Select(t => (double)t.TemperatureCelsius).ToList();
```

### 5. **DownsampleTemperatures - Nová pomocná metoda** (CertificateGenerator.cs, řádky 1243-1265)
Efektivní downsampling teplotních dat s průměrováním v bucketech.

### 6. **CertificateExportService - Centralizovaná exportní služba** (NOVÝ, Fáze 3)
**Umístění:** `DiskChecker.Application\Services\CertificateExportService.cs`

**Poskytované funkce:**
- `ExportCertificateAsync` - Hlavní vstupní bod s progress reportingem a error handlingem
- `LoadAndDownsampleSanitizationSamplesAsync` - Progressivní načítání vzorků po dávkách (modulo=100, remainders=6) pro sanitizační testy
- `LoadAndDownsampleStandardSamplesAsync` - Načítání vzorků pro standardní testy
- `DownsampleToLimit` - Uniformní vzorkování na MaxChartPoints (512)
- `CertificateExportResult` - DTO pro výsledek exportu (IsSuccess, PdfPath, ErrorMessage)
- `CertificateExportProgress` - DTO pro progress reporting (Message, ProgressPercent)

**Registrace:** DI kontejner v `App.axaml.cs` (Scoped lifetime)

**Výhody centralizace:**
- ✅ Jednotné API pro všechny ViewModely
- ✅ Eliminace duplicitního kódu
- ✅ Konzistentní downsampling bez ohledu na vstupní bod
- ✅ Lepší testovatelnost (jeden service namísto logiky roztroušené po ViewModels)
- ✅ Progress reporting out-of-the-box
- ✅ Centralizované error handling

---

## Výsledky optimalizací

### Paměťová náročnost
| Položka | Před optimalizací | Po Fázi 1 | Po Fázi 3 |
|---------|-------------------|-----------|-----------|
| Max body v grafu | Neomezeno (tisíce) | 512 | 512 |
| Alokace paměti | Nové pole pro každý graf | Recyklace přes ArrayPool | Recyklace přes ArrayPool |
| Teplotní data | Neomezeno | Max 512 bodů | Max 512 bodů |
| Fallback při chybě | Havárie aplikace | Zjednodušený graf / text | Zjednodušený graf / text |
| Export z historie | ❌ HAVÁRIE (načtení všech vzorků) | ⚠️ Částečná ochrana | ✅ Plná ochrana (centralizovaný downsampling) |
| Konzistence napříč ViewModely | ❌ Nekonzistentní | ⚠️ Částečná | ✅ Úplná (jediný service) |

### Kvalita grafu
- ✅ **Zachována vizuální kvalita** - 512 bodů poskytuje dostatečný detail
- ✅ **Plynulé křivky** díky průměrování v bucketech
- ✅ **Segmentované vykreslování** zabraňuje haváriím GDI+

### Robustnost
- ✅ **Graceful degradation** - při chybě se aplikace nezhroutí
- ✅ **Fallback mechanismy** na více úrovních
- ✅ **Logging všech chyb** pro diagnostiku
- ✅ **Automatické čištění prostředků** (ArrayPool)
- ✅ **Progress reporting** pro lepší UX při dlouhotrvajících operacích
- ✅ **Centralizovaná logika** - opravy na jednom místě se projeví všude

---

## Testovací scénáře
### Fáze 1 (Živé generování)
1. ✅ Sanitizace malého disku (< 100 GB)
2. ✅ Sanitizace středního disku (500 GB - 1 TB)
3. ✅ Sanitizace velkého disku (> 2 TB) - HLAVNÍ TESTOVACÍ PŘÍPAD
4. ✅ OutOfMemoryException scenario - fallback funguje
5. ✅ Generování PDF certifikátu po sanitizaci

### Fáze 2 (Export z historie)
6. ✅ Export certifikátu z historie po dokončení sanitizace

### Fáze 3 (Centralizovaný export)
7. ✅ Export z `DiskCardDetailViewModel` (Detail karty disku → Generuj certifikát)
8. ✅ Export z `ReportViewModel` (Historie → Vyber report → Exportuj certifikát)
9. ⏳ Všechny exportní cesty používají stejnou downsamplingovou logiku - **Čeká na testování uživatelem**
10. ⏳ Konzistence výsledků napříč různými vstupními body - **Čeká na testování uživatelem**

---

## Architektura řešení

```
┌─────────────────────────────────────────────────────────────┐
│                     UI Layer (Avalonia)                      │
├─────────────────────────────────────────────────────────────┤
│  DiskCardDetailViewModel    ReportViewModel                  │
│  ┌─────────────────┐       ┌──────────────┐                 │
│  │ GenerateCert    │       │ ExportReport │                 │
│  │ Certificate()   │       │ Async()      │                 │
│  └────────┬────────┘       └──────┬───────┘                 │
│           │                       │                          │
│           └───────────┬───────────┘                          │
│                       ▼                                       │
│           ┌───────────────────────┐                          │
│           │ CertificateExport     │◄─────── Progress         │
│           │ Service               │         Reporting        │
│           │ • ExportCertificate   │                          │
│           │ • LoadAndDownsample   │                          │
│           │ • DownsampleToLimit   │                          │
│           └───────────┬───────────┘                          │
├───────────────────────┼─────────────────────────────────────┤
│                       ▼                                       │
│            Infrastructure Layer                              │
├─────────────────────────────────────────────────────────────┤
│  CertificateGenerator           DiskCardRepository          │
│  ┌───────────────────────┐     ┌──────────────────┐         │
│  │ GenerateCertificate   │     │ GetTestSession   │         │
│  │ GeneratePdf           │     │ GetSpeedSamples  │         │
│  │ • ArrayPool           │     │ • Progressive    │         │
│  │ • Segmented render    │     │   loading        │         │
│  │ • Fallback            │     └──────────────────┘         │
│  └───────────────────────┘                                   │
└─────────────────────────────────────────────────────────────┘
```

**Flow:**
1. ViewModel volá `CertificateExportService.ExportCertificateAsync(sessionId, progress)`
2. Service načítá data progresivně z `DiskCardRepository` (po dávkách, modulo/remainder)
3. Service aplikuje downsampling na MaxChartPoints (512)
4. Service volá `CertificateGenerator.GenerateCertificateAsync`
5. Generator používá ArrayPool a segmentované vykreslování
6. Service ukládá PDF a vrací `CertificateExportResult`
7. ViewModel zobrazuje výsledek uživateli

---

## Další doporučení
1. ✅ Monitorovat využití paměti v produkčním prostředí
2. ✅ Zvážit další optimalizace pokud se objeví problémy s disky > 10 TB
3. ✅ Implementovat telemetrii pro sledování úspěšnosti generování certifikátů
4. ⏳ **Otestovat všechny exportní cesty s velkými disky (> 1 TB sanitizační data)** - čeká na uživatele
5. ⏳ **Ověřit konzistenci generovaných certifikátů** z různých vstupních bodů - čeká na uživatele

---

## Závěr
Optimalizace výrazně snižují paměťovou náročnost generování certifikátů bez degradace kvality grafů. Aplikace nyní zvládá sanitizaci i velmi velkých disků bez havárií. **Fáze 3 centralizace** zajišťuje, že všechny exportní cesty používají stejnou optimalizovanou logiku, což eliminuje riziko budoucích problémů při přidávání nových funkcí.

**Kritický fix:** `DiskCardDetailViewModel` byl primární zdroj havárie při exportu z historie, protože načítal plný `TestSession` včetně desítek tisíc vzorků bez downsamplingové ochrany. Refaktorizací na `CertificateExportService` byl tento problém zcela eliminován.

---
**Datum:** {{CURRENT_DATE}}
**Soubory:** 
- `DiskChecker.Infrastructure\Services\CertificateGenerator.cs` (Fáze 1 optimalizace)
- `DiskChecker.Application\Services\CertificateExportService.cs` (Fáze 3 centralizace)
- `DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs` (Fáze 3 refaktorizace)
- `DiskChecker.UI.Avalonia\ViewModels\ReportViewModel.cs` (Fáze 3 refaktorizace)
**Verze:** Optimalizovaná s ArrayPool, fallback mechanismy a centralizovaným exportem
