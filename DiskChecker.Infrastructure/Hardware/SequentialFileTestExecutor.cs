using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Executes disk tests by writing sequential files until disk is full.
/// Supports both Windows and Linux platforms.
/// </summary>
public class SequentialFileTestExecutor : ISurfaceTestExecutor
{
   private const byte PatternByte = 0xA5;
   private const long FileSize = 100L * 1024 * 1024; // 100 MB per file
   private const int HeaderSize = 1024; // 1KB header with metadata

   private readonly ISmartaProvider _smartaProvider;

   public SequentialFileTestExecutor(ISmartaProvider smartaProvider)
   {
      ArgumentNullException.ThrowIfNull(smartaProvider);
      _smartaProvider = smartaProvider;
   }

   /// <inheritdoc />
   public async Task<SurfaceTestResult> ExecuteAsync(
       SurfaceTestRequest request,
       IProgress<SurfaceTestProgress>? progress = null,
       CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(request);
      ArgumentNullException.ThrowIfNull(request.Drive);

      SurfaceTestResult result = new SurfaceTestResult
      {
         TestId = Guid.NewGuid().ToString(),
         Profile = request.Profile,
         Operation = request.Operation,
         StartedAtUtc = DateTime.UtcNow,
         SecureErasePerformed = false
      };

      // This executor is only for FullDiskSanitization
      if(request.Profile != SurfaceTestProfile.FullDiskSanitization)
      {
         result.ErrorCount = 1;
         result.Notes = "Tento executor je určen pouze pro FullDiskSanitization.";
         result.CompletedAtUtc = DateTime.UtcNow;
         return result;
      }

      // Step 1: Prepare the disk (if needed) and get test path
      var testPath = await PrepareDiskAsync(request.Drive, result, cancellationToken);
      if(string.IsNullOrEmpty(testPath))
      {
         result.ErrorCount = 1;
         result.Notes = $"CHYBA: Nepodařilo se připravit disk! {result.Notes} - DATA SE NEBUDOU PSÁT!";
         result.CompletedAtUtc = DateTime.UtcNow;
         return result;
      }

      // Verify path exists and is accessible
      try
      {
         if(!Directory.Exists(testPath))
         {
            result.ErrorCount = 1;
            result.Notes = $"CHYBA: Cesta {testPath} není přístupná!";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
         }
      }
      catch(Exception ex)
      {
         result.ErrorCount = 1;
         result.Notes = $"CHYBA: Nelze ověřit cestu {testPath} - {ex.Message}";
         result.CompletedAtUtc = DateTime.UtcNow;
         return result;
      }

      List<SurfaceTestSample> samples = new List<SurfaceTestSample>();
      Stopwatch totalStopwatch = Stopwatch.StartNew();
      long totalBytesWritten = 0;
      long totalBytesRead = 0;
      int filesWritten = 0;
      int filesVerified = 0;
      double peak = 0;
      double min = double.MaxValue;
      long totalBytesForCurrentPhase = 0;

      try
      {
         // === Collect Drive Metadata from SMART ===
         try
         {
            var smartData = await _smartaProvider.GetSmartaDataAsync(request.Drive.Path, cancellationToken);
            if(smartData != null)
            {
               result.DriveModel = smartData.DeviceModel ?? request.Drive.Name;
               result.DriveSerialNumber = smartData.SerialNumber;
               result.DriveManufacturer = ExtractManufacturer(smartData.DeviceModel);
               result.DriveTotalBytes = request.Drive.TotalSize;

               result.PowerOnHours = smartData.PowerOnHours > 0 ? smartData.PowerOnHours : null;
               result.CurrentTemperatureCelsius = smartData.Temperature > 0 ? (int)smartData.Temperature : null;
               result.ReallocatedSectors = smartData.ReallocatedSectorCount > 0 ? smartData.ReallocatedSectorCount : null;

               System.Diagnostics.Debug.WriteLine($"Drive metadata collected: {result.DriveModel} ({result.DriveTotalBytes} bytes)");
            }
         }
         catch(Exception ex)
         {
            System.Diagnostics.Debug.WriteLine($"Warning: Could not read SMART data: {ex.Message}");
         }

         // Get available space for test
         var testPathRoot = Path.GetPathRoot(testPath) ?? testPath;
         DriveInfo driveInfo = new DriveInfo(testPathRoot);
         var availableSpace = driveInfo.AvailableFreeSpace;
         long maxToWrite = Math.Min(availableSpace * 9 / 10, request.MaxBytesToTest ?? availableSpace);

         // === PHASE 1: WRITE ===
         result.Notes = "Fáze 1: Zápis souborů";
         var sampleStopwatch = Stopwatch.StartNew();
         var lastProgressReport = DateTime.UtcNow;  // NEW: Track last progress report time
         long sampleBytes = 0;
         int sampleBlocks = 0;
         int sampleIntervalBlocks = request.SampleIntervalBlocks > 0 ? request.SampleIntervalBlocks : 128;
         const int MaxMillisecondsBetweenProgressReports = 500;  // NEW: Report at least every 500ms

         while(totalBytesWritten < maxToWrite && !cancellationToken.IsCancellationRequested)
         {
            var fileName = Path.Combine(testPath, $"test_{filesWritten:D6}.bin");

            if(filesWritten == 0)
            {
               System.Diagnostics.Debug.WriteLine($"Writing first test file to: {fileName}");
            }

            try
            {
               using(FileStream fileStream = new FileStream(
                   fileName,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.Read,
                   1024 * 1024,
                   FileOptions.SequentialScan))
               {
                  var header = CreateHeader(filesWritten);
                  await fileStream.WriteAsync(header.AsMemory(), cancellationToken);
                  totalBytesWritten += header.Length;
                  totalBytesForCurrentPhase += header.Length;
                  sampleBytes += header.Length;

                  var dataBuffer = new byte[1024 * 1024];
                  Array.Fill(dataBuffer, (byte)0);

                  long bytesInFile = HeaderSize;
                  while(bytesInFile < FileSize && totalBytesWritten < maxToWrite && !cancellationToken.IsCancellationRequested)
                  {
                     var toWrite = (int)Math.Min(
                         Math.Min(dataBuffer.Length, maxToWrite - totalBytesWritten),
                         FileSize - bytesInFile);

                     if(toWrite <= 0) break;

                     await fileStream.WriteAsync(dataBuffer.AsMemory(0, toWrite), cancellationToken);
                     bytesInFile += toWrite;
                     totalBytesWritten += toWrite;
                     totalBytesForCurrentPhase += toWrite;
                     sampleBytes += toWrite;
                     sampleBlocks++;

                     // NEW: Report progress if EITHER interval reached OR time-based threshold exceeded
                     bool blockIntervalReached = sampleBlocks >= sampleIntervalBlocks;
                     bool timeSinceLastReport = (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports;
                     
                     if((blockIntervalReached || timeSinceLastReport) && sampleStopwatch.Elapsed.TotalSeconds > 0)
                     {
                        ReportProgressSample(
                            sampleBytes, request.BlockSizeBytes, totalBytesForCurrentPhase, 
                            maxToWrite, sampleStopwatch, samples, progress, result, ref peak, ref min,
                            phaseProgress: totalBytesForCurrentPhase * 50.0 / maxToWrite);
                        
                        sampleBytes = 0;
                        sampleBlocks = 0;
                        sampleStopwatch.Restart();
                        lastProgressReport = DateTime.UtcNow;  // NEW: Update last report time
                     }
                  }

                  await fileStream.FlushAsync(cancellationToken);
                  filesWritten++;
               }
            }
            catch(IOException)
            {
               result.Notes = $"Disk je zaplný. Vytvořeno {filesWritten} testovacích souborů.";
               break;
            }
            catch(Exception ex)
            {
               result.ErrorCount++;
               result.Notes = $"Chyba zápisu: {ex.Message}";

               if(result.ErrorCount > 5)
                  break;
            }
         }

         // Final sample for write phase
         if(sampleBytes > 0 && sampleStopwatch.Elapsed.TotalSeconds > 0)
         {
            ReportProgressSample(
                sampleBytes, request.BlockSizeBytes, totalBytesForCurrentPhase,
                maxToWrite, sampleStopwatch, samples, progress, result, ref peak, ref min,
                phaseProgress: totalBytesForCurrentPhase * 50.0 / maxToWrite);
         }

         // === PHASE 2: VERIFY ===
         if(result.ErrorCount == 0 && request.Operation == SurfaceTestOperation.WriteZeroFill)
         {
            result.Notes = "Fáze 2: Ověřování souborů";
            totalBytesForCurrentPhase = 0;
            sampleBytes = 0;
            sampleBlocks = 0;
            sampleStopwatch.Restart();
            lastProgressReport = DateTime.UtcNow;  // NEW: Reset timer for verify phase

            for(int i = 0; i < filesWritten && !cancellationToken.IsCancellationRequested; i++)
            {
               var fileName = Path.Combine(testPath, $"test_{i:D6}.bin");

               try
               {
                  using(FileStream fileStream = new FileStream(
                      fileName,
                      FileMode.Open,
                      FileAccess.Read,
                      FileShare.Read,
                      1024 * 1024,
                      FileOptions.SequentialScan))
                  {
                     var buffer = new byte[1024 * 1024];
                     int bytesRead;

                     while((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                     {
                        totalBytesRead += bytesRead;
                        totalBytesForCurrentPhase += bytesRead;
                        sampleBytes += bytesRead;
                        sampleBlocks++;

                        // Verify content (after header should be zeros)
                        if(fileStream.Position > HeaderSize)
                        {
                           for(int j = 0; j < bytesRead; j++)
                           {
                              if(buffer[j] != 0)
                              {
                                 result.ErrorCount++;
                                 break;
                              }
                           }
                        }

                        // NEW: Report progress if EITHER interval reached OR time-based threshold exceeded
                        bool blockIntervalReached = sampleBlocks >= sampleIntervalBlocks;
                        bool timeSinceLastReport = (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports;
                        
                        if((blockIntervalReached || timeSinceLastReport) && sampleStopwatch.Elapsed.TotalSeconds > 0)
                        {
                           ReportProgressSample(
                               sampleBytes, request.BlockSizeBytes, totalBytesForCurrentPhase,
                               maxToWrite, sampleStopwatch, samples, progress, result, ref peak, ref min,
                               phaseProgress: 50.0 + (totalBytesForCurrentPhase * 50.0 / maxToWrite));
                           
                           sampleBytes = 0;
                           sampleBlocks = 0;
                           sampleStopwatch.Restart();
                           lastProgressReport = DateTime.UtcNow;  // NEW: Update last report time
                        }
                     }

                     filesVerified++;
                  }
               }
               catch(Exception ex)
               {
                  result.ErrorCount++;
                  result.Notes = $"Chyba ověření: {ex.Message}";
               }
            }

            // Final sample for verify phase
            if(sampleBytes > 0 && sampleStopwatch.Elapsed.TotalSeconds > 0)
            {
               ReportProgressSample(
                   sampleBytes, request.BlockSizeBytes, totalBytesForCurrentPhase,
                   maxToWrite, sampleStopwatch, samples, progress, result, ref peak, ref min,
                   phaseProgress: 50.0 + (totalBytesForCurrentPhase * 50.0 / maxToWrite));
            }
         }

         result.Notes = $"Hotovo. Souborů: {filesWritten} (ověřeno {filesVerified}). Chyb: {result.ErrorCount}";
         result.SecureErasePerformed = true;
      }
      catch(Exception ex)
      {
         result.ErrorCount++;
         result.Notes = $"Chyba: {ex.Message}";
      }
      finally
      {
         // Clean up test files
         try
         {
            if(Directory.Exists(testPath))
            {
               foreach(var file in Directory.GetFiles(testPath, "test_*.bin"))
               {
                  File.Delete(file);
               }
            }
         }
         catch
         {
            // Ignore cleanup errors
         }
      }

      totalStopwatch.Stop();
      result.CompletedAtUtc = DateTime.UtcNow;
      result.TotalBytesTested = totalBytesRead > 0 ? totalBytesRead : totalBytesWritten;
      result.Samples = samples;

      if(totalStopwatch.Elapsed.TotalSeconds > 0)
      {
         var totalBytesProcessed = totalBytesWritten + totalBytesRead;
         if(totalBytesProcessed > 0)
         {
            result.AverageSpeedMbps = totalBytesProcessed / (1024.0 * 1024.0) / totalStopwatch.Elapsed.TotalSeconds;
         }
      }

      result.PeakSpeedMbps = peak == double.MaxValue ? 0 : peak;
      result.MinSpeedMbps = min == double.MaxValue ? 0 : min;

      return result;
   }

   /// <summary>
   /// Reports progress sample with phase-aware percentage.
   /// </summary>
   private static void ReportProgressSample(
       long sampleBytes,
       int blockSizeBytes,
       long phaseBytesProcessed,
       long totalBytesInPhase,
       Stopwatch sampleStopwatch,
       List<SurfaceTestSample> samples,
       IProgress<SurfaceTestProgress>? progress,
       SurfaceTestResult result,
       ref double peak,
       ref double min,
       double phaseProgress)
   {
      if(sampleStopwatch.Elapsed.TotalSeconds <= 0)
         return;

      var throughput = sampleBytes / (1024d * 1024d) / sampleStopwatch.Elapsed.TotalSeconds;

      var sample = new SurfaceTestSample
      {
         OffsetBytes = Math.Max(0, phaseBytesProcessed - sampleBytes),
         BlockSizeBytes = blockSizeBytes,
         ThroughputMbps = Math.Round(throughput, 2),
         TimestampUtc = DateTime.UtcNow,
         ErrorCount = 0
      };

      samples.Add(sample);
      peak = Math.Max(peak, sample.ThroughputMbps);
      min = Math.Min(min, sample.ThroughputMbps);

      progress?.Report(new SurfaceTestProgress
      {
         TestId = Guid.Parse(result.TestId),
         BytesProcessed = phaseBytesProcessed,
         PercentComplete = Math.Max(0, Math.Min(100, phaseProgress)),
         CurrentThroughputMbps = Math.Round(throughput, 2),
         TimestampUtc = DateTime.UtcNow
      });
   }

   /// <summary>
   /// Prepares the disk for testing. On Windows, uses diskpart; on Linux, uses existing mounted path.
   /// </summary>
   private async Task<string?> PrepareDiskAsync(CoreDriveInfo drive, SurfaceTestResult result, CancellationToken cancellationToken)
   {
      try
      {
         // Check if we're on Windows or Linux
         bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
         bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

         if(isLinux)
         {
            // On Linux, use the provided path directly (e.g., /mnt/disk, /media/user/disk)
            // Don't try to format - just use the path as-is
            if(Directory.Exists(drive.Path))
            {
               result.Notes = $"✓ Používám mount point: {drive.Path}";
               System.Diagnostics.Debug.WriteLine($"Using Linux mount point: {drive.Path}");
               return drive.Path;
            }

            // If the path doesn't exist, suggest mounting the disk
            result.Notes = $"⚠️  Disk není připojený. Prosím připojte disk ručně:\n" +
                          $"  sudo mkdir -p /mnt/testdisk\n" +
                          $"  sudo mount {drive.Path} /mnt/testdisk";
            System.Diagnostics.Debug.WriteLine($"Linux mount point not found: {drive.Path}");
            return null;
         }
         else if(isWindows)
         {
            // Windows: use diskpart to prepare the disk
            return await PrepareDiskWindowsAsync(drive, result, cancellationToken);
         }
         else
         {
            // Unknown platform
            result.Notes = "CHYBA: Neznámý operační systém. Podporovány jsou pouze Windows a Linux.";
            return null;
         }
      }
      catch(Exception ex)
      {
         result.Notes = $"CHYBA: {ex.Message}";
         return null;
      }
   }

   /// <summary>
   /// Windows-specific disk preparation using diskpart.
   /// </summary>
   private async Task<string?> PrepareDiskWindowsAsync(CoreDriveInfo drive, SurfaceTestResult result, CancellationToken cancellationToken)
   {
      try
      {
         // Check if already mounted
         if(drive.Path.Length == 3 && char.IsLetter(drive.Path[0]) && drive.Path[1] == ':')
         {
            var driveLetter = drive.Path[0].ToString().ToUpperInvariant();
            result.Notes = $"✓ Disk je již připojen na {driveLetter}: - přeskakuji formátování";
            System.Diagnostics.Debug.WriteLine($"Drive already mounted: {driveLetter}");
            return driveLetter + ":";
         }

         var driveNumber = new string(drive.Path.Where(char.IsDigit).ToArray());
         if(string.IsNullOrEmpty(driveNumber))
         {
            result.Notes = "CHYBA: Nelze určit číslo disku";
            return null;
         }

         result.Notes = "Příprava disku...";
         var drivesBefore = GetAvailableDriveLetters();
         var uniqueLabel = GenerateUniqueVolumeLabel();

         var diskpartScript = $@"list disk
select disk {driveNumber}
clean
create partition primary
select partition 1
format fs=ntfs quick label={uniqueLabel}
assign
exit";

         result.Notes = $"Spouštím diskpart...";

         ProcessStartInfo psi = new ProcessStartInfo
         {
            FileName = "diskpart",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
         };

         using Process? process = Process.Start(psi);
         if(process == null)
         {
            result.Notes = "CHYBA: Nelze spustit diskpart";
            return null;
         }

         using(var writer = process.StandardInput)
         {
            foreach(var line in diskpartScript.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
               if(!string.IsNullOrEmpty(line))
                  await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
         }

         var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
         var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
         await process.WaitForExitAsync(cancellationToken);

         if(process.ExitCode != 0)
         {
            result.Notes = $"CHYBA diskpart: {errors}";
            return null;
         }

         await Task.Delay(3000, cancellationToken);

         string? newDrive = await FindNewDriveLetterWithRetryAsync(drivesBefore, cancellationToken);

         if(string.IsNullOrEmpty(newDrive))
         {
            newDrive = FindDriveByVolumeLabel(uniqueLabel);
            if(string.IsNullOrEmpty(newDrive))
            {
               result.Notes = $"⚠️  Nepodařilo se automaticky detekovat písmeno disku. Hledaný label: {uniqueLabel}";
               return null;
            }
         }

         result.Notes = $"✓ Disk připraven na {newDrive}:";
         return newDrive + ":";
      }
      catch(Exception ex)
      {
         result.Notes = $"CHYBA: {ex.Message}";
         return null;
      }
   }

   private byte[] CreateHeader(int fileNumber)
   {
      var header = new byte[HeaderSize];
      Array.Fill(header, (byte)0);
      Array.Copy(BitConverter.GetBytes(fileNumber), 0, header, 0, 4);
      Array.Copy(BitConverter.GetBytes(DateTime.UtcNow.Ticks), 0, header, 4, 8);
      return header;
   }

   private async Task<string?> FindNewDriveLetterWithRetryAsync(List<string> drivesBefore, CancellationToken cancellationToken)
   {
      for(int attempt = 0; attempt < 3; attempt++)
      {
         var drivesAfter = GetAvailableDriveLetters();
         var newDrive = drivesAfter.Except(drivesBefore).FirstOrDefault();
         if(!string.IsNullOrEmpty(newDrive))
            return newDrive;

         if(attempt < 2)
            await Task.Delay(1000, cancellationToken);
      }
      return null;
   }

   private List<string> GetAvailableDriveLetters()
   {
      try
      {
         return DriveInfo.GetDrives()
             .Select(d => d.Name.Substring(0, 1).ToUpperInvariant())
             .OrderBy(x => x)
             .ToList();
      }
      catch
      {
         return new List<string>();
      }
   }

   private string? FindDriveByVolumeLabel(string targetLabel)
   {
      try
      {
         return DriveInfo.GetDrives()
             .FirstOrDefault(d => d.IsReady && d.VolumeLabel.Equals(targetLabel, StringComparison.OrdinalIgnoreCase))?
             .Name.Substring(0, 1)
             .ToUpperInvariant();
      }
      catch
      {
         return null;
      }
   }

   private string GenerateUniqueVolumeLabel()
   {
      var guid = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
      var timestamp = DateTime.Now.ToString("yyyyMMdd").Substring(2);
      return $"DISKTEST_{timestamp}_{guid}";
   }

   private static string? ExtractManufacturer(string? modelNumber)
   {
      if(string.IsNullOrEmpty(modelNumber))
         return null;

      var upper = modelNumber.ToUpperInvariant();

      return upper switch
      {
         var x when x.StartsWith("ST", StringComparison.Ordinal) => "Seagate",
         var x when x.StartsWith("WD", StringComparison.Ordinal) => "Western Digital",
         var x when x.StartsWith("SAMSUNG", StringComparison.Ordinal) => "Samsung",
         var x when x.StartsWith("INTEL", StringComparison.Ordinal) => "Intel",
         var x when x.StartsWith("TOSHIBA", StringComparison.Ordinal) => "Toshiba",
         var x when x.StartsWith("KINGSTON", StringComparison.Ordinal) => "Kingston",
         var x when x.StartsWith("CRUCIAL", StringComparison.Ordinal) => "Crucial",
         var x when x.StartsWith("SK HYNIX", StringComparison.Ordinal) => "SK Hynix",
         var x when x.StartsWith("HITACHI", StringComparison.Ordinal) => "Hitachi",
         var x when x.StartsWith("MAXTOR", StringComparison.Ordinal) => "Maxtor",
         var x when x.StartsWith("ADATA", StringComparison.Ordinal) => "ADATA",
         var x when x.StartsWith("SANDISK", StringComparison.Ordinal) => "SanDisk",
         _ => null
      };
   }
}
