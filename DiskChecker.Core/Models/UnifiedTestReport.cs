namespace DiskChecker.Core.Models;

/// <summary>
/// Jednotný report model pro všechny typy testů - SMART, Surface, a kombinované.
/// Umožňuje uložení a rekonstrukci testů s grafickými daty a SmartCTL výstupem.
/// </summary>
public class UnifiedTestReport
{
   /// <summary>
   /// Unikátní identifikátor reportu.
   /// </summary>
   public Guid ReportId { get; set; } = Guid.NewGuid();

   /// <summary>
   /// Typ testu - SMART, Surface, SurfaceWithSmart, SurfaceComplete (s sanitací).
   /// </summary>
   public string TestType { get; set; } = "SMART";

   /// <summary>
   /// Stav testu - InProgress, Completed, Failed.
   /// </summary>
   public string TestStatus { get; set; } = "Completed";

   /// <summary>
   /// Čas spuštění testu (UTC).
   /// </summary>
   public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

   /// <summary>
   /// Čas dokončení testu (UTC).
   /// </summary>
   public DateTime CompletedAtUtc { get; set; }

   /// <summary>
   /// Doba trvání testu.
   /// </summary>
   public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

   // === DISK INFORMATION ===

   /// <summary>
   /// Identifikátor disku.
   /// </summary>
   public string DiskPath { get; set; } = string.Empty;

   /// <summary>
   /// Název disku (např. "SSD Samsung 1TB").
   /// </summary>
   public string DiskName { get; set; } = string.Empty;

   /// <summary>
   /// Model disku.
   /// </summary>
   public string? DiskModel { get; set; }

   /// <summary>
   /// Sériové číslo disku.
   /// </summary>
   public string? SerialNumber { get; set; }

   /// <summary>
   /// Výrobce disku.
   /// </summary>
   public string? Manufacturer { get; set; }

   /// <summary>
   /// Firmware verze.
   /// </summary>
   public string? FirmwareVersion { get; set; }

   /// <summary>
   /// Kapacita disku v bytech.
   /// </summary>
   public long TotalCapacityBytes { get; set; }

   // === SMART DATA ===

   /// <summary>
   /// SMART data v okamžiku testu.
   /// </summary>
   public SmartaData? SmartaDataAtTest { get; set; }

   /// <summary>
   /// Kvalita/hodnocení SMART dat.
   /// </summary>
   public QualityRating? QualityRating { get; set; }

   /// <summary>
   /// SMART atributy.
   /// </summary>
   public List<SmartaAttributeItem> SmartAttributes { get; set; } = [];

   /// <summary>
   /// Raw výstup z smartctl -x -json pro archivaci.
   /// </summary>
   public string? SmartctlRawJson { get; set; }

   // === SURFACE TEST DATA ===

   /// <summary>
   /// Celkem zpracované bajty během surface testu.
   /// </summary>
   public long BytesProcessed { get; set; }

   /// <summary>
   /// Počet chyb během surface testu.
   /// </summary>
   public int SurfaceTestErrors { get; set; }

   /// <summary>
   /// Průměrná rychlost zápisu (MB/s).
   /// </summary>
   public double AverageWriteSpeedMbps { get; set; }

   /// <summary>
   /// Průměrná rychlost čtení (MB/s).
   /// </summary>
   public double AverageReadSpeedMbps { get; set; }

   /// <summary>
   /// Maximální rychlost čtení (MB/s).
   /// </summary>
   public double MaxReadSpeedMbps { get; set; }

   /// <summary>
   /// Minimální rychlost čtení (MB/s).
   /// </summary>
   public double MinReadSpeedMbps { get; set; }

   /// <summary>
   /// Vzorky rychlostí během testu (pro graf).
   /// </summary>
   public List<ReportSpeedSample> SpeedSamples { get; set; } = [];

   /// <summary>
   /// Vzorky teploty během testu (pro graf).
   /// </summary>
   public List<ReportTemperatureSample> TemperatureSamples { get; set; } = [];

   // === METADATA ===

   /// <summary>
   /// Poznámka/popis reportu.
   /// </summary>
   public string? Notes { get; set; }

   /// <summary>
   /// Uživatel který test spustil.
   /// </summary>
   public string? RunByUser { get; set; }

   /// <summary>
   /// Verze aplikace DiskChecker.
   /// </summary>
   public string AppVersion { get; set; } = "1.0.0";

   /// <summary>
   /// Režim tisku: Minimal (A10 štítek), Standard (A5), Detailed (A4).
   /// </summary>
   public string PrintMode { get; set; } = "Standard";

   /// <summary>
   /// Barvy používané v reportu (pro zajištění konzistence tisknutí).
   /// </summary>
   public Dictionary<string, string> ThemeColors { get; set; } = new();
}

/// <summary>
/// Vzorek rychlosti během testu pro report (pojmenováno ReportSpeedSample aby se nekonfliktovalo se SpeedSample).
/// </summary>
public class ReportSpeedSample
{
   /// <summary>
   /// Čas vzorku od začátku testu (v sekundách).
   /// </summary>
   public double ElapsedSeconds { get; set; }

   /// <summary>
   /// Propustnost v MB/s.
   /// </summary>
   public double ThroughputMbps { get; set; }

   /// <summary>
   /// Typ operace: 0=zápis, 1=čtení.
   /// </summary>
   public int OperationType { get; set; }

   /// <summary>
   /// Počet chyb v tomto vzorku.
   /// </summary>
   public int ErrorsInSample { get; set; }
}

/// <summary>
/// Vzorek teploty během testu pro report.
/// </summary>
public class ReportTemperatureSample
{
   /// <summary>
   /// Čas vzorku od začátku testu (v sekundách).
   /// </summary>
   public double ElapsedSeconds { get; set; }

   /// <summary>
   /// Teplota v °C.
   /// </summary>
   public double TemperatureCelsius { get; set; }

   /// <summary>
   /// Čas vzorku (absolutní).
   /// </summary>
   public DateTime TimestampUtc { get; set; }
}

/// <summary>
/// Konfigurace labelu pro tisk - velikost a obsah se adaptuje.
/// </summary>
public class LabelPrintConfiguration
{
   /// <summary>
   /// Velikost štítku: Minimal (A10, 105x148mm), Small (A9), Standard (A7), Large (A5), XL (A4).
   /// </summary>
   public string LabelSize { get; set; } = "Standard"; // A7

   /// <summary>
   /// Jestli se má tisk připravit v barevném formátu.
   /// </summary>
   public bool UseColors { get; set; } = true;

   /// <summary>
   /// Rozlišení tisku v DPI.
   /// </summary>
   public int DpiResolution { get; set; } = 300;

   /// <summary>
   /// Jestli se má tisk připravit pro tiskárnu štítků (vs. běžný tisk).
   /// </summary>
   public bool ForLabelPrinter { get; set; } = true;
}
