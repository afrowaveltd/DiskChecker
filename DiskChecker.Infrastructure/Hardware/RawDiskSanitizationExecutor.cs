using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Executes full disk sanitization using RAW sector write (not filesystem).
/// This executor unmounts the disk and writes directly to sectors.
/// </summary>
public class RawDiskSanitizationExecutor : ISurfaceTestExecutor
{
   private readonly ISmartaProvider _smartaProvider;

   // Win32 API imports for raw disk access
#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments
   [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
   private static extern SafeFileHandle CreateFile(
       string lpFileName,
       uint dwDesiredAccess,
       uint dwShareMode,
       IntPtr lpSecurityAttributes,
       uint dwCreationDisposition,
       uint dwFlagsAndAttributes,
       IntPtr hTemplateFile);
#pragma warning restore CA2101

   [DllImport("kernel32.dll", SetLastError = true)]
   private static extern bool SetFilePointerEx(
       IntPtr hFile,
       long liDistanceToMove,
       out long lpNewFilePointer,
       uint dwMoveMethod);

   [DllImport("kernel32.dll", SetLastError = true)]
   private static extern bool WriteFile(
       IntPtr hFile,
       IntPtr lpBuffer,
       uint nNumberOfBytesToWrite,
       out uint lpNumberOfBytesWritten,
       IntPtr lpOverlapped);

   [DllImport("kernel32.dll", SetLastError = true)]
   private static extern bool DeviceIoControl(
       SafeFileHandle hDevice,
       uint dwIoControlCode,
       IntPtr lpInBuffer,
       uint nInBufferSize,
       IntPtr lpOutBuffer,
       uint nOutBufferSize,
       out uint lpBytesReturned,
       IntPtr lpOverlapped);

   [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
   private static extern bool DeviceIoControlRaw(
       IntPtr hDevice,
       uint dwIoControlCode,
       IntPtr lpInBuffer,
       uint nInBufferSize,
       IntPtr lpOutBuffer,
       uint nOutBufferSize,
       out uint lpBytesReturned,
       IntPtr lpOverlapped);

   // Disk geometry structures
   [StructLayout(LayoutKind.Sequential)]
   private struct DISK_GEOMETRY_EX
   {
       public DISK_GEOMETRY Geometry;
       public long DiskSize;
       [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
       public byte[] Data;
   }

   [StructLayout(LayoutKind.Sequential)]
   private struct DISK_GEOMETRY
   {
       public long Cylinders;
       public uint MediaType;
       public uint TracksPerCylinder;
       public uint SectorsPerTrack;
       public uint BytesPerSector;
   }

   // Constants
   private const uint GENERIC_READ = 0x80000000;
   private const uint GENERIC_WRITE = 0x40000000;
   private const uint OPEN_EXISTING = 3;
   private const uint FILE_SHARE_READ = 0x00000001;
   private const uint FILE_SHARE_WRITE = 0x00000002;
   private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
   private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
   private const uint FSCTL_LOCK_VOLUME = 0x00090018;
   private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
   private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;

   // Disk management IOCTLs
   private const uint IOCTL_DISK_SET_DISK_ATTRIBUTES = 0x0007C0F4;
   private const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
   private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;

   // SetFilePointerEx constants
   private const uint FILE_BEGIN = 0;
   private const uint FILE_CURRENT = 1;

   // Disk attributes
   private const uint DISK_ATTRIBUTE_OFFLINE = 0x0000000000000001;
   private const uint DISK_ATTRIBUTE_READ_ONLY = 0x0000000000000002;

   [StructLayout(LayoutKind.Sequential)]
   private struct SET_DISK_ATTRIBUTES
   {
      public uint Version;
      public byte Persist;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
      public byte[] Reserved1;
      public ulong Attributes;
      public ulong AttributesMask;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
      public uint[] Reserved2;
   }

   public RawDiskSanitizationExecutor(ISmartaProvider smartaProvider)
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

      // Only for FullDiskSanitization
      if(request.Profile != SurfaceTestProfile.FullDiskSanitization)
      {
         result.ErrorCount = 1;
         result.Notes = "Tento executor je určen pouze pro FullDiskSanitization.";
         result.CompletedAtUtc = DateTime.UtcNow;
         return result;
      }

      // Get physical drive path (\\.\PHYSICALDRIVE2 format)
      System.Diagnostics.Debug.WriteLine($"RawDiskSanitizationExecutor: Input drive.Path={request.Drive.Path}, drive.Name={request.Drive.Name}");

      string? physicalDrivePath = await GetPhysicalDrivePathAsync(request.Drive, result);

      System.Diagnostics.Debug.WriteLine($"RawDiskSanitizationExecutor: GetPhysicalDrivePathAsync returned: {physicalDrivePath ?? "NULL"}");

      if(string.IsNullOrEmpty(physicalDrivePath))
      {
         result.ErrorCount = 1;
         result.CompletedAtUtc = DateTime.UtcNow;
         System.Diagnostics.Debug.WriteLine($"RawDiskSanitizationExecutor: FAILED - physicalDrivePath is null or empty");
         return result;
      }

      // Perform raw disk sanitization
      await SanitizeDiskRawAsync(physicalDrivePath, request, result, progress, cancellationToken);

      return result;
   }

   /// <summary>
   /// Gets the physical drive path and ensures all partitions are dismounted.
   /// For sanitization, we MUST use physical drive (\\.\PHYSICALDRIVE2), not volume letters.
   /// </summary>
   private async Task<string?> GetPhysicalDrivePathAsync(CoreDriveInfo drive, SurfaceTestResult result)
   {
      // For sanitization, we need physical drive path
      string? physicalDrivePath = null;

      if(drive.Path.StartsWith(@"\\.\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
      {
         // Already in correct format
         physicalDrivePath = drive.Path;
      }
      else if(drive.Path.Length >= 2 && char.IsLetter(drive.Path[0]) && drive.Path[1] == ':')
      {
         // Convert drive letter to physical drive number
         char driveLetter = drive.Path[0];
         int? diskIndex = await FindPhysicalDriveForLetterAsync(driveLetter);

         if(diskIndex.HasValue)
         {
            physicalDrivePath = $@"\\.\PHYSICALDRIVE{diskIndex.Value}";
         }
      }
      else
      {
          var match = Regex.Match(drive.Path, @"PhysicalDrive(?<index>\d+)", RegexOptions.IgnoreCase);
          if(match.Success && int.TryParse(match.Groups["index"].Value, out int diskIndex))
          {
             physicalDrivePath = $@"\\.\PHYSICALDRIVE{diskIndex}";
          }
       }

      if(string.IsNullOrEmpty(physicalDrivePath))
      {
         result.Notes = $"❌ CHYBA: Nelze určit physical drive path z: {drive.Path}";
         return null;
      }

      result.Notes = $"✓ Používám physical drive: {physicalDrivePath}";
      return physicalDrivePath;
   }

   /// <summary>
   /// Finds physical drive index for a given drive letter using WMIC.
   /// </summary>
   private async Task<int?> FindPhysicalDriveForLetterAsync(char driveLetter)
   {
      try
      {
         ProcessStartInfo psi = new ProcessStartInfo
         {
            FileName = "wmic",
            Arguments = $"partition where \"DriveLetter='{driveLetter}:'\" get DiskIndex",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using Process? process = Process.Start(psi);
         if(process == null) return null;

         string output = await process.StandardOutput.ReadToEndAsync();
         await process.WaitForExitAsync();

         // Parse output: "DiskIndex\n2\n"
         string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
         if(lines.Length >= 2 && int.TryParse(lines[1].Trim(), out int diskIndex))
         {
            return diskIndex;
         }

         return null;
      }
      catch
      {
         return null;
      }
   }

   /// <summary>
   /// Finds and dismounts ALL partitions on a physical drive.
   /// This is REQUIRED before writing to physical drive.
   /// Uses multiple methods to ensure all partitions are found.
   /// </summary>
   private async Task<List<string>> FindAndDismountAllPartitionsAsync(int diskIndex, StreamWriter? logWriter)
   {
      List<string> dismountedPartitions = new List<string>();
      HashSet<string> foundPartitions = new HashSet<string>();

      if(logWriter != null)
      {
         await logWriter.WriteLineAsync($"[INFO] Searching for partitions on disk {diskIndex}...");
         await logWriter.FlushAsync(CancellationToken.None);
      }

      // Method 1: Try WMIC first
      try
      {
         ProcessStartInfo psi = new ProcessStartInfo
         {
            FileName = "wmic",
            Arguments = $"partition where \"DiskIndex={diskIndex}\" get DriveLetter",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using Process? process = Process.Start(psi);
         if(process != null)
         {
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach(string line in lines)
            {
               string trimmed = line.Trim();
               if(trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
               {
                  string volumePath = $@"\\.\{trimmed[0]}:";
                  foundPartitions.Add(volumePath);

                  if(logWriter != null)
                  {
                     await logWriter.WriteLineAsync($"[INFO] WMIC found partition: {volumePath}");
                     await logWriter.FlushAsync(CancellationToken.None);
                  }
               }
            }
         }
      }
      catch(Exception ex)
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] WMIC failed: {ex.Message}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
      }

      // Method 2: Try PowerShell Get-Partition
      try
      {
         ProcessStartInfo psi = new ProcessStartInfo
         {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"Get-Partition -DiskNumber {diskIndex} | Select-Object -ExpandProperty DriveLetter\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using Process? process = Process.Start(psi);
         if(process != null)
         {
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach(string line in lines)
            {
               string trimmed = line.Trim();
               if(trimmed.Length == 1 && char.IsLetter(trimmed[0]))
               {
                  string volumePath = $@"\\.\{trimmed[0]}:";
                  if(foundPartitions.Add(volumePath))
                  {
                     if(logWriter != null)
                     {
                        await logWriter.WriteLineAsync($"[INFO] PowerShell found partition: {volumePath}");
                        await logWriter.FlushAsync(CancellationToken.None);
                     }
                  }
               }
            }
         }
      }
      catch(Exception ex)
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] PowerShell Get-Partition failed: {ex.Message}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
      }

      // Method 3: Use PowerShell to find ALL volumes (including those without drive letters)
      try
      {
         var psi = new ProcessStartInfo
         {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"Get-Partition -DiskNumber {diskIndex} | ForEach-Object {{ Get-Volume -Partition $_ | Select-Object -ExpandProperty Path }}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using var process = Process.Start(psi);
         if(process != null)
         {
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach(string line in lines)
            {
               string trimmed = line.Trim();
               if(!string.IsNullOrEmpty(trimmed) && trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
               {
                  // Volume GUID path like \\?\Volume{guid}\
                  if(foundPartitions.Add(trimmed))
                  {
                     if(logWriter != null)
                     {
                        await logWriter.WriteLineAsync($"[INFO] PowerShell found volume GUID: {trimmed}");
                        await logWriter.FlushAsync(CancellationToken.None);
                     }
                  }
               }
            }
         }
      }
      catch(Exception ex)
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] PowerShell Get-Volume failed: {ex.Message}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
      }

      // Now dismount all found partitions
      foreach(string volumePath in foundPartitions)
      {
         if(await DismountSinglePartitionAsync(volumePath, logWriter))
         {
            dismountedPartitions.Add(volumePath);
         }
      }

      if(logWriter != null)
      {
         await logWriter.WriteLineAsync($"[INFO] Total partitions found: {foundPartitions.Count}, dismounted: {dismountedPartitions.Count}");
         await logWriter.FlushAsync(CancellationToken.None);
      }

      return dismountedPartitions;
   }

   /// <summary>
   /// Dismounts a single partition by opening it and calling FSCTL_LOCK_VOLUME + FSCTL_DISMOUNT_VOLUME.
   /// </summary>
   private async Task<bool> DismountSinglePartitionAsync(string volumePath, StreamWriter? logWriter)
   {
      SafeFileHandle? volumeHandle = null;

      try
      {
         // Open volume with exclusive access
         volumeHandle = CreateFile(
             volumePath,
             GENERIC_READ | GENERIC_WRITE,
             0, // Exclusive
             IntPtr.Zero,
             OPEN_EXISTING,
             0, // No special flags needed for dismount
             IntPtr.Zero);

         if(volumeHandle.IsInvalid)
         {
            if(logWriter != null)
            {
               await logWriter.WriteLineAsync($"[WARNING] Cannot open {volumePath} for dismount: Error {Marshal.GetLastWin32Error()}");
               await logWriter.FlushAsync(CancellationToken.None);
            }
            return false;
         }

         // Lock volume
         if(!DeviceIoControl(volumeHandle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
         {
            if(logWriter != null)
            {
               await logWriter.WriteLineAsync($"[WARNING] Cannot lock {volumePath}: Error {Marshal.GetLastWin32Error()}");
               await logWriter.FlushAsync(CancellationToken.None);
            }
            return false;
         }

         // Dismount volume
         if(!DeviceIoControl(volumeHandle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
         {
            if(logWriter != null)
            {
               await logWriter.WriteLineAsync($"[WARNING] Cannot dismount {volumePath}: Error {Marshal.GetLastWin32Error()}");
               await logWriter.FlushAsync(CancellationToken.None);
            }

            // Unlock before returning
            DeviceIoControl(volumeHandle, FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            return false;
         }

         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[SUCCESS] Dismounted partition: {volumePath}");
            await logWriter.FlushAsync(CancellationToken.None);
         }

         return true;
      }
      catch(Exception ex)
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[ERROR] DismountSinglePartitionAsync({volumePath}): {ex.Message}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
         return false;
      }
      finally
      {
         volumeHandle?.Dispose();
      }
   }

   /// <summary>
   /// Performs raw sector write to sanitize the disk.
   /// </summary>
   private async Task SanitizeDiskRawAsync(
       string physicalDrivePath,
       SurfaceTestRequest request,
       SurfaceTestResult result,
       IProgress<SurfaceTestProgress>? progress,
       CancellationToken cancellationToken)
   {
      Stopwatch stopwatch = Stopwatch.StartNew();
      long totalBytes = request.Drive.TotalSize;
      long bytesWritten = 0;
      int errorCount = 0;
      List<SurfaceTestSample> samples = new List<SurfaceTestSample>();

      // Create debug log file in current directory
      string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"sanitization_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");
      StreamWriter? logWriter = null;

      try
      {
         logWriter = new StreamWriter(logFilePath, false, System.Text.Encoding.UTF8);
         await logWriter.WriteLineAsync($"=== Sanitization Debug Log ===");
         await logWriter.WriteLineAsync($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
         await logWriter.WriteLineAsync($"Physical Drive: {physicalDrivePath}");
         await logWriter.WriteLineAsync($"Partition Size (request): {FormatBytes(totalBytes)}");
         await logWriter.WriteLineAsync($"Log File: {logFilePath}");
         await logWriter.WriteLineAsync();
         await logWriter.FlushAsync(CancellationToken.None);
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"Failed to create log file: {ex.Message}");
      }

      result.Notes = $"Příprava disku pro sanitizaci...\nLog: {logFilePath}";
      
      // === CRITICAL: Get ACTUAL physical disk size ===
      // request.Drive.TotalSize is often PARTITION size, not disk size!
      if (logWriter != null)
      {
         await logWriter.WriteLineAsync($"[INFO] Querying actual physical disk size...");
         await logWriter.FlushAsync(CancellationToken.None);
      }
      
      long? actualDiskSize = await GetPhysicalDiskSizeAsync(physicalDrivePath, logWriter);
      
      if (actualDiskSize.HasValue && actualDiskSize.Value > totalBytes)
      {
         if (logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] Partition size ({FormatBytes(totalBytes)}) < Physical disk size ({FormatBytes(actualDiskSize.Value)})");
            await logWriter.WriteLineAsync($"[INFO] Using PHYSICAL DISK SIZE for sanitization");
            await logWriter.FlushAsync(CancellationToken.None);
         }
         
         totalBytes = actualDiskSize.Value;
         result.Notes += $"\n⚠️ Detekováno: Partition {FormatBytes(request.Drive.TotalSize)}, Fyzický disk {FormatBytes(totalBytes)}";
         result.Notes += $"\n✓ Použiju skutečnou velikost physical disku!";
      }
      else if (actualDiskSize.HasValue)
      {
         if (logWriter != null)
         {
            await logWriter.WriteLineAsync($"[INFO] Physical disk size matches partition size: {FormatBytes(actualDiskSize.Value)}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
          
         totalBytes = actualDiskSize.Value;
      }
      else
      {
         if (logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] Could not detect physical disk size, using partition size: {FormatBytes(totalBytes)}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
          
         result.Notes += $"\n⚠️ Nepodařilo se detekovat velikost physical disku, používám partition size";
      }

      // Extract disk number from physical drive path
      string diskNumberStr = new string(physicalDrivePath.Where(char.IsDigit).ToArray());
      if(!int.TryParse(diskNumberStr, out int diskNumber))
      {
         result.ErrorCount = 1;
         result.Notes = $"❌ CHYBA: Nelze určit číslo disku z: {physicalDrivePath}";
         result.CompletedAtUtc = DateTime.UtcNow;
         return;
      }

      // CRITICAL: Dismount ALL partitions on this physical drive BEFORE opening it
      if(logWriter != null)
      {
         await logWriter.WriteLineAsync($"[INFO] Dismounting all partitions on disk {diskNumber}...");
         await logWriter.FlushAsync(CancellationToken.None);
      }

      var dismountedPartitions = await FindAndDismountAllPartitionsAsync(diskNumber, logWriter);

      if(dismountedPartitions.Count == 0)
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[WARNING] No partitions found or dismounted on disk {diskNumber}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
         result.Notes += $"\n⚠️ Žádné partitions nenalezeny nebo odpojeny - disk možná nemá filesystem";
      }
      else
      {
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[SUCCESS] Dismounted {dismountedPartitions.Count} partition(s): {string.Join(", ", dismountedPartitions)}");
            await logWriter.FlushAsync(CancellationToken.None);
         }
         result.Notes += $"\n✓ Odpojeno {dismountedPartitions.Count} partition(s)";
      }

      // CRITICAL: Set disk OFFLINE to prevent Windows from auto-remounting partitions!
      if(logWriter != null)
      {
         await logWriter.WriteLineAsync($"[INFO] Setting disk {diskNumber} offline...");
         await logWriter.FlushAsync(CancellationToken.None);
      }

      if(!await SetDiskOfflineAsync(diskNumber, logWriter))
      {
         result.ErrorCount = 1;
         result.Notes += $"\n❌ CHYBA: Nelze nastavit disk jako offline. Windows může znovu připojit partitions během sanitizace!";
         result.CompletedAtUtc = DateTime.UtcNow;
         return;
      }

      result.Notes += $"\n✓ Disk nastaven jako offline (Windows jej znovu nepřipojí)";

      result.Notes += $"\n\nOtevírám přímý přístup k physical drive...";

      // Open raw disk handle
      SafeFileHandle? diskHandle = null;
      try
      {
         diskHandle = CreateFile(
             physicalDrivePath,
             GENERIC_READ | GENERIC_WRITE,
             0, // EXCLUSIVE ACCESS - no sharing!
             IntPtr.Zero,
             OPEN_EXISTING,
             FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
             IntPtr.Zero);

         if(diskHandle.IsInvalid)
         {
            result.ErrorCount = 1;
            result.Notes = $"❌ CHYBA: Nelze otevřít physical drive {physicalDrivePath}. Win32 Error: {Marshal.GetLastWin32Error()}";
            result.CompletedAtUtc = DateTime.UtcNow;

            if(logWriter != null)
            {
               await logWriter.WriteLineAsync($"[FATAL] CreateFile failed: Error {Marshal.GetLastWin32Error()}");
               await logWriter.FlushAsync(CancellationToken.None);
            }

            return;
         }

         if(logWriter != null)
         {
            await logWriter.WriteLineAsync($"[SUCCESS] Physical drive opened successfully");
            await logWriter.FlushAsync(CancellationToken.None);
         }

         result.Notes += "\n✓ Physical drive otevřen pro raw write";
         result.Notes += "\n\nZahajuji přepis sektorů nulami...";

         // Write zeros to all sectors
         // Use 1 MB buffer aligned to 4096 bytes (sector size for AFN drives)
         const int SECTOR_SIZE = 4096;
         const int BUFFER_SIZE_MB = 1;
         const int bufferSize = BUFFER_SIZE_MB * 1024 * 1024;

         // Allocate aligned buffer using Marshal.AllocHGlobal
         IntPtr alignedBuffer = Marshal.AllocHGlobal(bufferSize + SECTOR_SIZE);
         IntPtr alignedBufferPtr = new IntPtr((alignedBuffer.ToInt64() + SECTOR_SIZE - 1) & ~(SECTOR_SIZE - 1));

         try
         {
            // Fill buffer with zeros
            unsafe
            {
               byte* bufferPtr = (byte*)alignedBufferPtr;
               for(int i = 0; i < bufferSize; i++)
               {
                  bufferPtr[i] = 0;
               }
            }

            Stopwatch sampleStopwatch = Stopwatch.StartNew();
            long sampleBytes = 0;

            while(bytesWritten < totalBytes && !cancellationToken.IsCancellationRequested)
            {
               // Calculate how much to write (must be sector-aligned)
               long remaining = totalBytes - bytesWritten;
               int toWrite = (int)Math.Min(bufferSize, remaining);

               // Round down to sector boundary
               toWrite = toWrite / SECTOR_SIZE * SECTOR_SIZE;

               if(toWrite <= 0) break;

               try
               {
                  // Set file pointer to current offset BEFORE writing
                  long newPosition;
                  if(!SetFilePointerEx(diskHandle.DangerousGetHandle(), bytesWritten, out newPosition, FILE_BEGIN))
                  {
                     int seekError = Marshal.GetLastWin32Error();
                     errorCount++;

                     if(logWriter != null)
                     {
                        await logWriter.WriteLineAsync($"[ERROR #{errorCount}] SetFilePointerEx failed at offset {bytesWritten}: Error {seekError} (0x{seekError:X})");
                        await logWriter.FlushAsync(CancellationToken.None);
                     }

                     if(errorCount > 10)
                     {
                        result.Notes = $"❌ CHYBA: Nelze nastavit file pointer. Error {seekError}";
                        break;
                     }

                     bytesWritten += toWrite;
                     continue;
                  }

                  // Verify pointer was set correctly
                  if(newPosition != bytesWritten)
                  {
                     errorCount++;

                     if(logWriter != null)
                     {
                        await logWriter.WriteLineAsync($"[ERROR #{errorCount}] SetFilePointerEx returned wrong position: expected {bytesWritten}, got {newPosition}");
                        await logWriter.FlushAsync(CancellationToken.None);
                     }

                     if(errorCount > 10)
                     {
                        result.Notes = $"❌ CHYBA: File pointer nesedí. Expected {bytesWritten}, got {newPosition}";
                        break;
                     }

                     bytesWritten += toWrite;
                     continue;
                  }

                  // Now write at current position (synchronous, no OVERLAPPED)
                  uint bytesWrittenThisCall = 0;
                  bool success = WriteFile(
                      diskHandle.DangerousGetHandle(),
                      alignedBufferPtr,
                      (uint)toWrite,
                      out bytesWrittenThisCall,
                      IntPtr.Zero);  // NO OVERLAPPED!

                  if(!success)
                  {
                     errorCount++;
                     int lastError = Marshal.GetLastWin32Error();
                     string errorMessage = $"WriteFile failed at offset {bytesWritten}: Win32 Error {lastError} (0x{lastError:X})";
                     System.Diagnostics.Debug.WriteLine(errorMessage);

                     // Log to file
                     if(logWriter != null)
                     {
                        await logWriter.WriteLineAsync($"[ERROR #{errorCount}] {errorMessage}");
                        await logWriter.WriteLineAsync($"  - Timestamp: {DateTime.Now:HH:mm:ss.fff}");
                        await logWriter.WriteLineAsync($"  - File pointer was: {newPosition} (0x{newPosition:X})");
                        await logWriter.WriteLineAsync($"  - Requested write size: {toWrite:N0} bytes");
                        await logWriter.WriteLineAsync($"  - Buffer aligned: {alignedBufferPtr.ToInt64() % SECTOR_SIZE == 0}");
                        await logWriter.WriteLineAsync($"  - Size aligned: {toWrite % SECTOR_SIZE == 0}");
                        await logWriter.FlushAsync(CancellationToken.None);
                     }

                     // Add error to notes for user visibility
                     if(errorCount == 1)
                     {
                        result.Notes = $"⚠️ První chyba zápisu:\n{errorMessage}\n";
                        result.Notes += $"📝 Debug log: {logFilePath}\n";
                     }

                     if(errorCount > 10)
                     {
                        result.Notes += $"\n❌ Příliš mnoho chyb při zápisu ({errorCount}). Test přerušeno.\n";
                        result.Notes += $"Poslední chyba: {errorMessage}";
                        break;
                     }

                     // Skip this chunk and continue
                     bytesWritten += toWrite;
                     continue;
                  }

                  bytesWritten += bytesWrittenThisCall;
                  sampleBytes += bytesWrittenThisCall;

                  // Log first few successful writes for debugging
                  if(logWriter != null && bytesWritten <= 10 * 1024 * 1024)
                  {
                     await logWriter.WriteLineAsync($"[SUCCESS] WriteFile at offset {bytesWritten - bytesWrittenThisCall} (0x{bytesWritten - bytesWrittenThisCall:X})");
                     await logWriter.WriteLineAsync($"  - Bytes written: {bytesWrittenThisCall:N0}");
                     await logWriter.WriteLineAsync($"  - File pointer after write: {bytesWritten} (0x{bytesWritten:X})");
                     await logWriter.FlushAsync(CancellationToken.None);
                  }

                  // Report progress every 128 MB or 500ms
                  if(sampleBytes >= 128 * 1024 * 1024 || sampleStopwatch.Elapsed.TotalMilliseconds >= 500)
                  {
                     double throughput = sampleBytes / (1024.0 * 1024.0) / sampleStopwatch.Elapsed.TotalSeconds;

                     samples.Add(new SurfaceTestSample
                     {
                        OffsetBytes = bytesWritten - sampleBytes,
                        BlockSizeBytes = bufferSize,
                        ThroughputMbps = Math.Round(throughput, 2),
                        TimestampUtc = DateTime.UtcNow,
                        ErrorCount = 0
                     });

                     progress?.Report(new SurfaceTestProgress
                     {
                        TestId = Guid.Parse(result.TestId),
                        BytesProcessed = bytesWritten,
                        PercentComplete = Math.Min(100, bytesWritten * 100.0 / totalBytes),
                        CurrentThroughputMbps = Math.Round(throughput, 2),
                        TimestampUtc = DateTime.UtcNow
                     });

                     sampleBytes = 0;
                     sampleStopwatch.Restart();
                  }
               }
               catch(Exception ex)
               {
                  errorCount++;
                  System.Diagnostics.Debug.WriteLine($"Error writing at offset {bytesWritten}: {ex.Message}");

                  if(errorCount > 10)
                  {
                     result.Notes = $"❌ Příliš mnoho chyb při zápisu ({errorCount}). Test přerušen.";
                     break;
                  }

                  // Skip and continue
                  bytesWritten += SECTOR_SIZE;
               }
            }
         }
         finally
         {
            // Free aligned buffer
            if(alignedBuffer != IntPtr.Zero)
            {
               Marshal.FreeHGlobal(alignedBuffer);
            }
         }

         // Unlock volume
         UnlockVolume(diskHandle);

         result.SecureErasePerformed = true;
         result.TotalBytesTested = bytesWritten;
         result.ErrorCount = errorCount;
         result.Samples = samples;
         result.CompletedAtUtc = DateTime.UtcNow;

         if(stopwatch.Elapsed.TotalSeconds > 0)
         {
            result.AverageSpeedMbps = bytesWritten / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds;
         }

         if(samples.Any())
         {
            result.PeakSpeedMbps = samples.Max(s => s.ThroughputMbps);
            result.MinSpeedMbps = samples.Min(s => s.ThroughputMbps);
         }

         StringBuilder finalNotes = new System.Text.StringBuilder();

         // Preserve error details if any exist
         if(!string.IsNullOrEmpty(result.Notes))
         {
            finalNotes.AppendLine("🔍 DETAILY CHYB:");
            finalNotes.AppendLine(result.Notes);
            finalNotes.AppendLine();
         }

         finalNotes.AppendLine($"✓ Sanitizace dokončena");
         finalNotes.AppendLine($"📊 Statistiky:");
         finalNotes.AppendLine($"  • Zapsáno: {FormatBytes(bytesWritten)}");
         finalNotes.AppendLine($"  • Průměrná rychlost: {result.AverageSpeedMbps:F1} MB/s");
         finalNotes.AppendLine($"  • Chyb: {errorCount}");

         if(errorCount == 0)
         {
            finalNotes.AppendLine();
            finalNotes.AppendLine($"✅ VÝSLEDEK: Disk byl úspěšně přepsán nulami");
         }
         else
         {
            finalNotes.AppendLine();
            finalNotes.AppendLine($"⚠️ VAROVÁNÍ: {errorCount} chyb při zápisu - možné vadné sektory");
         }

         result.Notes = finalNotes.ToString();
      }
      catch(Exception ex)
      {
         result.ErrorCount++;
         result.Notes = $"❌ CHYBA: {ex.Message}";
         result.CompletedAtUtc = DateTime.UtcNow;
      }
      finally
      {
         diskHandle?.Dispose();

         // Close log file
         if(logWriter != null)
         {
            await logWriter.WriteLineAsync();
            await logWriter.WriteLineAsync($"=== Sanitization Completed ===");
            await logWriter.WriteLineAsync($"Total bytes written: {FormatBytes(bytesWritten)}");
            await logWriter.WriteLineAsync($"Total errors: {errorCount}");
            await logWriter.WriteLineAsync($"Duration: {stopwatch.Elapsed}");
            await logWriter.FlushAsync(CancellationToken.None);
            logWriter.Dispose();

            result.Notes += $"\n\n📄 Kompletní debug log: {logFilePath}";
         }
      }
   }

   /// <summary>
   /// Sets a disk offline to prevent Windows from auto-mounting partitions.
   /// This is CRITICAL after dismounting - otherwise Windows will remount on first write!
   /// </summary>
   private async Task<bool> SetDiskOfflineAsync(int diskNumber, StreamWriter? logWriter)
   {
       SafeFileHandle? diskHandle = null;
       
       try
       {
           var physicalDrivePath = $@"\\.\PHYSICALDRIVE{diskNumber}";
           
           // Open disk (NOT with exclusive access - we'll use it later for writing)
           diskHandle = CreateFile(
               physicalDrivePath,
               GENERIC_READ | GENERIC_WRITE,
               FILE_SHARE_READ | FILE_SHARE_WRITE, // Allow sharing
               IntPtr.Zero,
               OPEN_EXISTING,
               0, // No special flags
               IntPtr.Zero);

           if (diskHandle.IsInvalid)
           {
               if (logWriter != null)
               {
                   await logWriter.WriteLineAsync($"[ERROR] Cannot open {physicalDrivePath} to set offline: Error {Marshal.GetLastWin32Error()}");
                   await logWriter.FlushAsync(CancellationToken.None);
               }
               return false;
           }

           // Prepare SET_DISK_ATTRIBUTES structure
           var diskAttribs = new SET_DISK_ATTRIBUTES
           {
               Version = (uint)Marshal.SizeOf<SET_DISK_ATTRIBUTES>(),
               Persist = 0, // Don't persist across reboots
               Reserved1 = new byte[3],
               Attributes = DISK_ATTRIBUTE_OFFLINE,
               AttributesMask = DISK_ATTRIBUTE_OFFLINE,
               Reserved2 = new uint[4]
           };

           int structSize = Marshal.SizeOf<SET_DISK_ATTRIBUTES>();
           IntPtr pDiskAttribs = Marshal.AllocHGlobal(structSize);
           
           try
           {
               Marshal.StructureToPtr(diskAttribs, pDiskAttribs, false);

               uint bytesReturned;
               bool success = DeviceIoControlRaw(
                   diskHandle.DangerousGetHandle(),
                   IOCTL_DISK_SET_DISK_ATTRIBUTES,
                   pDiskAttribs,
                   (uint)structSize,
                   IntPtr.Zero,
                   0,
                   out bytesReturned,
                   IntPtr.Zero);

               if (!success)
               {
                   int error = Marshal.GetLastWin32Error();
                   if (logWriter != null)
                   {
                       await logWriter.WriteLineAsync($"[ERROR] IOCTL_DISK_SET_DISK_ATTRIBUTES failed: Error {error} (0x{error:X})");
                       await logWriter.FlushAsync(CancellationToken.None);
                   }
                   return false;
               }

               if (logWriter != null)
               {
                   await logWriter.WriteLineAsync($"[SUCCESS] Disk {diskNumber} set offline");
                   await logWriter.FlushAsync(CancellationToken.None);
               }

               return true;
           }
           finally
           {
               Marshal.FreeHGlobal(pDiskAttribs);
           }
       }
       catch (Exception ex)
       {
           if (logWriter != null)
           {
               await logWriter.WriteLineAsync($"[ERROR] SetDiskOfflineAsync: {ex.Message}");
               await logWriter.FlushAsync(CancellationToken.None);
           }
           return false;
       }
       finally
       {
           diskHandle?.Dispose();
       }
   }
   
   /// <summary>
   /// Gets the ACTUAL physical disk size (not partition size).
   /// This is critical - request.Drive.TotalSize is often PARTITION size!
   /// </summary>
   private async Task<long?> GetPhysicalDiskSizeAsync(string physicalDrivePath, StreamWriter? logWriter)
   {
       SafeFileHandle? diskHandle = null;
       
       try
       {
           // Open disk for query
           diskHandle = CreateFile(
               physicalDrivePath,
               GENERIC_READ,
               FILE_SHARE_READ | FILE_SHARE_WRITE,
               IntPtr.Zero,
               OPEN_EXISTING,
               0,
               IntPtr.Zero);

           if (diskHandle.IsInvalid)
           {
               if (logWriter != null)
               {
                   await logWriter.WriteLineAsync($"[ERROR] Cannot open {physicalDrivePath} to query size: Error {Marshal.GetLastWin32Error()}");
                   await logWriter.FlushAsync(CancellationToken.None);
               }
               return null;
           }

           // Allocate buffer for DISK_GEOMETRY_EX
           int bufferSize = 256;  // Plenty of space
           IntPtr pGeometry = Marshal.AllocHGlobal(bufferSize);
           
           try
           {
               // Zero out buffer
               for (int i = 0; i < bufferSize; i++)
               {
                   Marshal.WriteByte(pGeometry, i, 0);
               }
               
               uint bytesReturned;
               bool success = DeviceIoControlRaw(
                   diskHandle.DangerousGetHandle(),
                   IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                   IntPtr.Zero,
                   0,
                   pGeometry,
                   (uint)bufferSize,
                   out bytesReturned,
                   IntPtr.Zero);

               if (!success)
               {
                   int error = Marshal.GetLastWin32Error();
                   if (logWriter != null)
                   {
                       await logWriter.WriteLineAsync($"[WARNING] IOCTL_DISK_GET_DRIVE_GEOMETRY_EX failed: Error {error} (0x{error:X})");
                       await logWriter.FlushAsync(CancellationToken.None);
                   }
                   return null;
               }

               // Read DiskSize field
               // DISK_GEOMETRY structure layout:
               //   Cylinders (LARGE_INTEGER) = 8 bytes
               //   MediaType (DWORD) = 4 bytes  
               //   TracksPerCylinder (DWORD) = 4 bytes
               //   SectorsPerTrack (DWORD) = 4 bytes
               //   BytesPerSector (DWORD) = 4 bytes
               // Total DISK_GEOMETRY = 24 bytes
               //
               // DISK_GEOMETRY_EX layout:
               //   Geometry (DISK_GEOMETRY) = 24 bytes
               //   DiskSize (LARGE_INTEGER) = 8 bytes at offset 24
               
               const int DISK_SIZE_OFFSET = 24;
               long diskSize = Marshal.ReadInt64(pGeometry, DISK_SIZE_OFFSET);
               
               if (logWriter != null)
               {
                   await logWriter.WriteLineAsync($"[SUCCESS] Physical disk size detected: {FormatBytes(diskSize)} ({diskSize} bytes)");
                   await logWriter.WriteLineAsync($"   - BytesReturned: {bytesReturned}");
                   
                   // Debug: Read first few fields
                   long cylinders = Marshal.ReadInt64(pGeometry, 0);
                   uint mediaType = (uint)Marshal.ReadInt32(pGeometry, 8);
                   uint tracksPerCylinder = (uint)Marshal.ReadInt32(pGeometry, 12);
                   uint sectorsPerTrack = (uint)Marshal.ReadInt32(pGeometry, 16);
                   uint bytesPerSector = (uint)Marshal.ReadInt32(pGeometry, 20);
                   
                   await logWriter.WriteLineAsync($"   - Cylinders: {cylinders}");
                   await logWriter.WriteLineAsync($"   - MediaType: {mediaType}");
                   await logWriter.WriteLineAsync($"   - TracksPerCylinder: {tracksPerCylinder}");
                   await logWriter.WriteLineAsync($"   - SectorsPerTrack: {sectorsPerTrack}");
                   await logWriter.WriteLineAsync($"   - BytesPerSector: {bytesPerSector}");
                   
                   await logWriter.FlushAsync(CancellationToken.None);
               }
               
               // Sanity check
               if (diskSize <= 0 || diskSize > 20L * 1024 * 1024 * 1024 * 1024) // 20 TB max
               {
                   if (logWriter != null)
                   {
                       await logWriter.WriteLineAsync($"[ERROR] Detected disk size {diskSize} is invalid (out of range)");
                       await logWriter.FlushAsync(CancellationToken.None);
                   }
                   return null;
               }

               return diskSize;
           }
           finally
           {
               Marshal.FreeHGlobal(pGeometry);
           }
       }
       catch (Exception ex)
       {
           if (logWriter != null)
           {
               await logWriter.WriteLineAsync($"[ERROR] GetPhysicalDiskSizeAsync: {ex.Message}");
               await logWriter.FlushAsync(CancellationToken.None);
           }
           return null;
       }
       finally
       {
           diskHandle?.Dispose();
       }
   }
   
   /// <summary>
   /// Locks and dismounts a volume to prevent filesystem access.
   /// </summary>
   private bool LockAndDismountVolume(SafeFileHandle handle)
   {
      try
      {
         // Lock volume
         if(!DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
         {
            return false;
         }

         // Dismount volume
         if(!DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
         {
            return false;
         }

         return true;
      }
      catch
      {
         return false;
      }
   }

   /// <summary>
   /// Unlocks a volume after sanitization.
   /// </summary>
   private bool UnlockVolume(SafeFileHandle handle)
   {
      try
      {
         return DeviceIoControl(handle, FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
      }
      catch
      {
         return false;
      }
   }

   private static string? ExtractManufacturer(string? modelNumber)
   {
      if(string.IsNullOrEmpty(modelNumber))
         return null;

      string upper = modelNumber.ToUpperInvariant();

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

   private static string FormatBytes(long byteCount)
   {
      string[] suffixes = { "B", "kB", "MB", "GB", "TB" };
      double size = byteCount;
      int order = 0;

      while(size >= 1024 && order < suffixes.Length - 1)
      {
         order++;
         size = size / 1024;
      }

      return $"{size:0.##} {suffixes[order]}";
   }
}
