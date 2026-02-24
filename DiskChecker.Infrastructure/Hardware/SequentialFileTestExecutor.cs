using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System.Diagnostics;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Executes disk tests by writing sequential files until disk is full.
/// Uses diskpart to prepare the physical disk properly.
/// </summary>
public class SequentialFileTestExecutor : ISurfaceTestExecutor
{
   private const byte PatternByte = 0xA5;
   private const long FileSize = 100L * 1024 * 1024; // 100 MB per file
   private const int HeaderSize = 1024; // 1KB header with metadata

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
         Drive = request.Drive,
         Technology = request.Technology,
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

      // Step 1: Prepare the disk (clean and format)
      var driveLetter = await PrepareDiskAsync(request.Drive, result, cancellationToken);
      if(string.IsNullOrEmpty(driveLetter))
      {
         result.ErrorCount = 1;
         result.Notes = $"CHYBA: Nepodařilo se připravit disk! {result.Notes} - DATA SE NEBUDOU PSÁT!";
         result.CompletedAtUtc = DateTime.UtcNow;
         return result;
      }

      // Build test path from drive letter
      var testPath = driveLetter + ":";

      // Verify drive is ready
      try
      {
         if(!Directory.Exists(testPath))
         {
            result.ErrorCount = 1;
            result.Notes = $"CHYBA: Disk {driveLetter}: není přístupný!";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
         }
      }
      catch(Exception ex)
      {
         result.ErrorCount = 1;
         result.Notes = $"CHYBA: Nelze ověřit disk {driveLetter}: - {ex.Message}";
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

      try
      {
         // === Collect Drive Metadata from SMART ===
         try
         {
            var smartData = await _smartaProvider.GetSmartDataAsync(request.Drive);
            if(smartData?.SmartaAttributes != null)
            {
               result.DriveModel = smartData.SmartaAttributes.ModelNumber ?? request.Drive.Name;
               result.DriveSerialNumber = smartData.SmartaAttributes.SerialNumber;
               result.DriveManufacturer = ExtractManufacturer(smartData.SmartaAttributes.ModelNumber);
               result.DriveInterface = smartData.SmartaAttributes.FormFactor?.ToString();
               result.DriveTotalBytes = request.Drive.TotalSize;

               // Extract SMART values
               if(smartData.SmartaAttributes.Attributes?.Count > 0)
               {
                  // Power-on hours (attribute 9)
                  var powerOnAttr = smartData.SmartaAttributes.Attributes
                      .FirstOrDefault(a => a.AttributeId == 9);
                  if(powerOnAttr?.CurrentValue > 0)
                  {
                     result.PowerOnHours = powerOnAttr.RawValue;
                  }

                  // Temperature (attribute 194 - HDA Temp, 190 - Airflow Temp)
                  var tempAttr = smartData.SmartaAttributes.Attributes
                      .FirstOrDefault(a => a.AttributeId == 194 || a.AttributeId == 190);
                  if(tempAttr?.CurrentValue > 0)
                  {
                     result.CurrentTemperatureCelsius = (int)tempAttr.RawValue;
                  }

                  // Reallocated sectors (attribute 5)
                  var reallocAttr = smartData.SmartaAttributes.Attributes
                      .FirstOrDefault(a => a.AttributeId == 5);
                  if(reallocAttr?.CurrentValue > 0)
                  {
                     result.ReallocatedSectors = reallocAttr.RawValue;
                  }

                  // RPM for HDDs (attribute 3)
                  var rpmAttr = smartData.SmartaAttributes.Attributes
                      .FirstOrDefault(a => a.AttributeId == 3);
                  if(rpmAttr?.CurrentValue > 0)
                  {
                     result.DriveRpmOrNvmeSpeed = (int)rpmAttr.RawValue;
                  }
               }

               System.Diagnostics.Debug.WriteLine($"Drive metadata collected: {result.DriveModel} ({result.DriveTotalBytes} bytes)");
            }
         }
         catch(Exception ex)
         {
            // Non-critical - continue test even if SMART read fails
            System.Diagnostics.Debug.WriteLine($"Warning: Could not read SMART data: {ex.Message}");
         }

         // Get actual disk size to calculate how many files we can write
         // Use DriveInfo on root of mount point path
         var testPathRoot = Path.GetPathRoot(testPath) ?? "C:\\";
         DriveInfo driveInfo = new DriveInfo(testPathRoot);
         var availableSpace = driveInfo.AvailableFreeSpace;
         long maxToWrite = Math.Min(availableSpace * 9 / 10, request.MaxBytesToTest ?? availableSpace);

         result.Notes = "Fáze 1: Zápis souborů";

         // Phase 1: Write files until disk is full or max size reached
         while(totalBytesWritten < maxToWrite && !cancellationToken.IsCancellationRequested)
         {
            var fileName = Path.Combine(testPath, $"test_{filesWritten:D6}.bin");

            // Log first file being written
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
                  Stopwatch sampleStopwatch = Stopwatch.StartNew();

                  // Write header
                  var header = CreateHeader(filesWritten);
                  await fileStream.WriteAsync(header.AsMemory(), cancellationToken);
                  totalBytesWritten += header.Length;

                  // Write data (zeros)
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
                  }

                  await fileStream.FlushAsync(cancellationToken);
                  sampleStopwatch.Stop();

                  filesWritten++;

                  // Record sample
                  if(sampleStopwatch.Elapsed.TotalSeconds > 0)
                  {
                     var throughput = bytesInFile / (1024.0 * 1024.0) / sampleStopwatch.Elapsed.TotalSeconds;
                     peak = Math.Max(peak, throughput);
                     min = Math.Min(min, throughput);

                     samples.Add(new SurfaceTestSample
                     {
                        OffsetBytes = totalBytesWritten,
                        BlockSizeBytes = 1024 * 1024,
                        ThroughputMbps = Math.Round(throughput, 2),
                        TimestampUtc = DateTime.UtcNow,
                        ErrorCount = 0
                     });

                     var percentComplete = totalBytesWritten * 100.0 / maxToWrite;

                     progress?.Report(new SurfaceTestProgress
                     {
                        TestId = result.TestId,
                        BytesProcessed = totalBytesWritten,
                        PercentComplete = percentComplete,
                        CurrentThroughputMbps = Math.Round(throughput, 2),
                        TimestampUtc = DateTime.UtcNow
                     });
                  }
               }
            }
            catch(IOException)
            {
               // Disk full - expected end condition
               result.Notes = $"Disk je zaplný. Vytvořeno {filesWritten} testovacích souborů.";
               break;
            }
            catch(Exception ex)
            {
               result.ErrorCount++;
               result.Notes = $"Chyba: {ex.Message}";

               if(result.ErrorCount > 5)
               {
                  break;
               }
            }
         }

         // Phase 2: Verify files
         if(result.ErrorCount == 0 && request.Operation == SurfaceTestOperation.WriteZeroFill)
         {
            result.Notes = "Fáze 2: Ověřování souborů";
            Stopwatch verifyStopwatch = Stopwatch.StartNew();

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
                     }

                     filesVerified++;
                  }

                  // Report progress
                  if(filesWritten > 0)
                  {
                     progress?.Report(new SurfaceTestProgress
                     {
                        TestId = result.TestId,
                        BytesProcessed = totalBytesRead,
                        PercentComplete = Math.Min(100, 50 + (filesVerified * 50.0 / filesWritten)),
                        CurrentThroughputMbps = totalBytesRead > 0 && verifyStopwatch.Elapsed.TotalSeconds > 0
                             ? totalBytesRead / (1024.0 * 1024.0) / verifyStopwatch.Elapsed.TotalSeconds
                             : 0,
                        TimestampUtc = DateTime.UtcNow
                     });
                  }
               }
               catch(Exception ex)
               {
                  result.ErrorCount++;
                  result.Notes = $"Chyba ověření: {ex.Message}";
               }
            }

            verifyStopwatch.Stop();
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
            var cleanupPath = driveLetter + ":";
            if(Directory.Exists(cleanupPath))
            {
               foreach(var file in Directory.GetFiles(cleanupPath, "test_*.bin"))
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

      if(totalStopwatch.Elapsed.TotalSeconds > 0 && totalBytesWritten > 0)
      {
         result.AverageSpeedMbps = totalBytesWritten / (1024.0 * 1024.0) / totalStopwatch.Elapsed.TotalSeconds;
      }

      result.PeakSpeedMbps = peak == double.MaxValue ? 0 : peak;
      result.MinSpeedMbps = min == double.MaxValue ? 0 : min;

      return result;
   }

   /// <summary>
   /// Creates a header with metadata for the test file.
   /// </summary>
   private byte[] CreateHeader(int fileNumber)
   {
      var header = new byte[HeaderSize];
      Array.Fill(header, (byte)0);

      var numberBytes = BitConverter.GetBytes(fileNumber);
      Array.Copy(numberBytes, 0, header, 0, 4);

      var timestampBytes = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
      Array.Copy(timestampBytes, 0, header, 4, 8);

      var checksum = fileNumber ^ DateTime.UtcNow.Ticks.GetHashCode();
      var checksumBytes = BitConverter.GetBytes(checksum);
      Array.Copy(checksumBytes, 0, header, 12, 4);

      return header;
   }

   /// <summary>
   /// Prepares the disk by cleaning all partitions and creating a new NTFS partition.
   /// Uses delta method to find assigned drive letter - compares available drives before and after.
   /// </summary>
   private async Task<string?> PrepareDiskAsync(CoreDriveInfo drive, SurfaceTestResult result, CancellationToken cancellationToken)
   {
      try
      {
         // Check if this is already a mounted drive (e.g., "E:\") vs physical disk (e.g., "\\.\PHYSICALDRIVE2")
         if(drive.Path.Length == 3 && char.IsLetter(drive.Path[0]) && drive.Path[1] == ':')
         {
            // Already a mounted drive! Extract the letter and return it
            var driveLetter = drive.Path[0].ToString().ToUpperInvariant();
            result.Notes = $"✓ Disk je již připojen na {driveLetter}: - přeskakuji formátování";
            System.Diagnostics.Debug.WriteLine($"Drive already mounted: {driveLetter}");
            return driveLetter;
         }

         // Extract disk number from physical path (e.g., "\\.\PHYSICALDRIVE2" → "2")
         var driveNumber = new string(drive.Path.Where(char.IsDigit).ToArray());
         if(string.IsNullOrEmpty(driveNumber))
         {
            result.Notes = "CHYBA: Nelze určit číslo disku";
            return null;
         }

         result.Notes = "Příprava disku...";

         // Get drives BEFORE formatting
         var drivesBefore = GetAvailableDriveLetters();
         System.Diagnostics.Debug.WriteLine($"Drives before: {string.Join(", ", drivesBefore)}");
         result.Notes = $"Aktuální disky: {string.Join(", ", drivesBefore)}";

         // Generate unique volume label for identification
         var uniqueLabel = GenerateUniqueVolumeLabel();
         result.Notes = $"2️⃣  Generuji unikátní label: {uniqueLabel}";

         // Create diskpart script
         var diskpartScript = $@"list disk
select disk {driveNumber}
clean
create partition primary
select partition 1
format fs=ntfs quick label={uniqueLabel}
assign
exit";

         try
         {
            result.Notes = $"3️⃣  Spouštím diskpart...";

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
               result.Notes = "CHYBA: Nelze spustit diskpart - proces vrátil null";
               return null;
            }

            // Write script to stdin
            using(var writer = process.StandardInput)
            {
               foreach(var line in diskpartScript.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
               {
                  if(!string.IsNullOrEmpty(line))
                  {
                     await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                  }
               }
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            System.Diagnostics.Debug.WriteLine($"DiskPart Output:\n{output}");
            if(!string.IsNullOrEmpty(errors))
            {
               System.Diagnostics.Debug.WriteLine($"DiskPart Errors:\n{errors}");
            }

            // Check if requested disk exists in output
            if(!output.Contains($"Disk {driveNumber}", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA: Disk {driveNumber} nenalezen v diskpart! Disk je pravděpodobně vadný a Windows ho nereaguje.";
               return null;
            }

            // Check for specific error messages
            if(output.Contains("The disk you specified is not valid", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA: Disk {driveNumber} není validní. Diskpart ho nenašel. Disk je pravděpodobně vadný.";
               return null;
            }

            if(output.Contains("There is no disk selected", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA: Diskpart nemohl vybrat disk {driveNumber}. Disk nereaguje. Disk je kriticky poškozený.";
               return null;
            }

            if(output.Contains("no partition", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("no volume", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA: Nepodařilo se vytvořit partici na disku {driveNumber}. Disk je poškozený.";
               return null;
            }

            // Check for errors in output
            if(output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA diskpart: Disk nereaguje. Pravděpodobně vadný disk.";
               return null;
            }

            if(process.ExitCode != 0)
            {
               result.Notes = $"CHYBA diskpart (kód {process.ExitCode}): {errors}";
               return null;
            }

            // Check if assign was successful
            if(!output.Contains("assigned", StringComparison.OrdinalIgnoreCase))
            {
               result.Notes = $"CHYBA: Diskpart se nezdařilo přiřadit písmeno jednotky.";
               return null;
            }

            result.Notes = $"4️⃣  Čekám na stabilizaci (3 sekundy)...";
            // Wait for Windows to recognize and stabilize the new drive letter
            await Task.Delay(3000, cancellationToken);

            result.Notes = $"5️⃣  Hledám nové písmeno disku (delta metoda)...";

            // Use delta method to find the newly assigned drive letter with retry
            string? newDrive = await FindNewDriveLetterWithRetryAsync(drivesBefore, cancellationToken);

            if(string.IsNullOrEmpty(newDrive))
            {
               // Return special marker that tells caller to ask user
               result.Notes = $"⚠️  Nové písmeno se nepodařilo zjistit automaticky.\n" +
                             $"Disk byl naformátován s labelem: {uniqueLabel}\n" +
                             $"Prosím vyberte disk ze seznamu (bude obsahovat DISKTEST_)";
               return null; // Signal caller to ask user for disk selection
            }

            result.Notes = $"✓ Disk připraven na {newDrive}: (label: {uniqueLabel})";
            System.Diagnostics.Debug.WriteLine($"Disk formatted and assigned to: {newDrive}");

            return newDrive;
         }
         catch(Exception ex)
         {
            result.Notes = $"CHYBA diskpart: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"DiskPart Exception: {ex}");
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
   /// Finds new drive letter with retry logic (3 attempts with 1 second delays).
   /// </summary>
   private async Task<string?> FindNewDriveLetterWithRetryAsync(List<string> drivesBefore, CancellationToken cancellationToken)
   {
      const int maxRetries = 3;
      const int delayMs = 1000;

      for(int attempt = 0; attempt < maxRetries; attempt++)
      {
         try
         {
            var drivesAfter = GetAvailableDriveLetters();
            System.Diagnostics.Debug.WriteLine($"Attempt {attempt + 1}: Drives after: {string.Join(", ", drivesAfter)}");

            var newDrive = drivesAfter.Except(drivesBefore).FirstOrDefault();

            if(!string.IsNullOrEmpty(newDrive))
            {
               System.Diagnostics.Debug.WriteLine($"Found new drive on attempt {attempt + 1}: {newDrive}");
               return newDrive;
            }

            System.Diagnostics.Debug.WriteLine($"Attempt {attempt + 1}: No new drive found yet, retrying...");

            if(attempt < maxRetries - 1)
            {
               await Task.Delay(delayMs, cancellationToken);
            }
         }
         catch(Exception ex)
         {
            System.Diagnostics.Debug.WriteLine($"Error on attempt {attempt + 1}: {ex.Message}");
            if(attempt < maxRetries - 1)
            {
               await Task.Delay(delayMs, cancellationToken);
            }
         }
      }

      return null;
   }

   /// <summary>
   /// Gets list of currently available drive letters (C:, D:, E:, etc.)
   /// </summary>
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

   /// <summary>
   /// Generates a unique volume label for the test partition to avoid collisions.
   /// Format: DISKTEST_yyMMdd_xxxxxxxx
   /// </summary>
   private string GenerateUniqueVolumeLabel()
   {
      var guid = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
      var timestamp = DateTime.Now.ToString("yyyyMMdd").Substring(2);
      return $"DISKTEST_{timestamp}_{guid}";
   }

   /// <summary>
   /// Extracts manufacturer from model string.
   /// Examples: "ST500DM002" → "Seagate", "WDC WD..." → "WDC", "Samsung SSD..." → "Samsung"
   /// </summary>
   private string? ExtractManufacturer(string? modelNumber)
   {
      if(string.IsNullOrEmpty(modelNumber))
         return null;

      var upper = modelNumber.ToUpperInvariant();

      return upper switch
      {
         _ when upper.StartsWith("ST") => "Seagate",
         _ when upper.StartsWith("WD") => "Western Digital",
         _ when upper.StartsWith("SAMSUNG") => "Samsung",
         _ when upper.StartsWith("INTEL") => "Intel",
         _ when upper.StartsWith("TOSHIBA") => "Toshiba",
         _ when upper.StartsWith("KINGSTON") => "Kingston",
         _ when upper.StartsWith("CRUCIAL") => "Crucial",
         _ when upper.StartsWith("SK HYNIX") => "SK Hynix",
         _ when upper.StartsWith("HITACHI") => "Hitachi",
         _ when upper.StartsWith("MAXTOR") => "Maxtor",
         _ when upper.StartsWith("ADATA") => "ADATA",
         _ when upper.StartsWith("SANDISK") => "SanDisk",
         _ => null
      };
   }
}
