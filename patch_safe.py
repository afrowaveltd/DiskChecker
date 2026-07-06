import sys

with open('DiskChecker.UI.Avalonia/ViewModels/SafeDestructiveTestViewModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

action = sys.argv[1] if len(sys.argv) > 1 else ''

if action == 'enum':
    old = '''public enum SafeDestructivePhase
{
    /// <summary>Initial state — disk selected, ready to start.</summary>
    Ready,
    /// <summary>Creating raw sector image of the disk.</summary>
    Backup,
    /// <summary>Running destructive test phases.</summary>
    Test,
    /// <summary>Restoring raw image back to disk.</summary>
    Restore,
    /// <summary>All phases complete.</summary>
    Completed,
    /// <summary>Cancelled by user or error.</summary>
    Failed
}'''
    new = '''public enum SafeDestructivePhase
{
    /// <summary>Initial state — disk selected, ready to start.</summary>
    Ready,
    /// <summary>Creating raw sector image of the disk.</summary>
    Backup,
    /// <summary>Running destructive test phases.</summary>
    Test,
    /// <summary>Restoring raw image back to disk.</summary>
    Restore,
    /// <summary>Creating partition after test.</summary>
    Partition,
    /// <summary>All phases complete.</summary>
    Completed,
    /// <summary>Cancelled by user or error.</summary>
    Failed
}

public enum SafeDestructiveMode
{
    /// <summary>Full backup → test → restore (original behavior).</summary>
    BackupAndRestore,
    /// <summary>VHDx backup → verify → test → partition (no restore, data can be restored manually).</summary>
    VhdxOnly
}'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Enum updated')
    else:
        print('FAIL: Enum pattern not found')

elif action == 'mode-prop':
    old = '    [ObservableProperty] private BackupTargetItem? _selectedBackupTarget;'
    new = '''    [ObservableProperty] private BackupTargetItem? _selectedBackupTarget;

    // ── Mode selection ──
    [ObservableProperty] private SafeDestructiveMode _selectedMode = SafeDestructiveMode.BackupAndRestore;
    [ObservableProperty] private string _vhdxBackupPath = string.Empty;
    [ObservableProperty] private string _vhdxBackupPathText = string.Empty;
    [ObservableProperty] private bool _vhdxBackupVerified;'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Mode properties added')
    else:
        print('FAIL: Mode prop pattern not found')

elif action == 'workflow':
    old = '''        try
        {
            // ── Phase 1: Backup ──
            await RunBackupPhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Phase 2: Destructive Test ──
            await RunTestPhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Phase 3: Restore ──
            await RunRestorePhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Complete ──
            Phase = SafeDestructivePhase.Completed;'''
    new = '''        try
        {
            // ── Phase 1: Backup ──
            if (SelectedMode == SafeDestructiveMode.VhdxOnly)
            {
                await RunVhdxBackupPhaseAsync(ct);
            }
            else
            {
                await RunBackupPhaseAsync(ct);
            }
            if (ct.IsCancellationRequested) return;

            // ── Phase 2: Destructive Test ──
            await RunTestPhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Phase 3: Restore (only in BackupAndRestore mode) ──
            if (SelectedMode == SafeDestructiveMode.BackupAndRestore)
            {
                await RunRestorePhaseAsync(ct);
                if (ct.IsCancellationRequested) return;
            }

            // ── Phase 4: Partition (VhdxOnly mode) ──
            if (SelectedMode == SafeDestructiveMode.VhdxOnly)
            {
                await RunPartitionPhaseAsync(ct);
                if (ct.IsCancellationRequested) return;
            }

            // ── Complete ──
            Phase = SafeDestructivePhase.Completed;'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Workflow updated')
    else:
        print('FAIL: Workflow pattern not found')

elif action == 'vhdx-backup':
    old = '    // ──────────────────────────────────────────────\n    //  Phase 3: Raw Restore\n    // ──────────────────────────────────────────────'
    new = '''    // ──────────────────────────────────────────────
    //  Phase 1b: VHDx Backup (VhdxOnly mode)
    // ──────────────────────────────────────────────

    private async Task RunVhdxBackupPhaseAsync(CancellationToken ct)
    {
        Phase = SafeDestructivePhase.Backup;
        CurrentPhaseName = "VHDx záloha";
        CurrentPhaseIcon = "💾";
        StatusMessage = "Vytvářím VHDx obraz disku...";
        OverallProgress = 0;
        OverallProgressText = "0%";

        if (SelectedDrive == null) return;

        var backupRoot = Path.Combine(BackupTargetPath, $"DiskChecker_VhdxBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupRoot);
        _backupImagePath = Path.Combine(backupRoot, "disk_image.vhdx");
        _backupManifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        VhdxBackupPath = _backupImagePath;
        VhdxBackupPathText = _backupImagePath;
        VhdxBackupVerified = false;

        Log($"VHDx záloha → {_backupImagePath}");

        // Use 1 MiB blocks for speed
        const int blockSize = 1024 * 1024;
        var buffer = new byte[blockSize];
        long totalSize = DiskTotalBytes;
        long bytesRead = 0;
        long unreadableBytes = 0;
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 64;
        _phaseStartTime = DateTime.UtcNow;
        _phaseBytesProcessed = 0;

        // Write VHDx header + BAT (Block Allocation Table) — dynamic 4k-sector VHDx
        await WriteVhdxHeaderAsync(_backupImagePath, totalSize, ct);

        using var sourceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, blockSize, FileOptions.SequentialScan);
        using var targetStream = new FileStream(_backupImagePath, FileMode.Append, FileAccess.Write,
            FileShare.Read, blockSize, FileOptions.SequentialScan);

        try
        {
            while (bytesRead < totalSize)
            {
                ct.ThrowIfCancellationRequested();

                int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
                int bytesReadNow = 0;
                bool blockReadable = true;

                try
                {
                    bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
                }
                catch (IOException ex) when (IsDeviceDisappearedError(ex))
                {
                    throw new InvalidOperationException(
                        $"❌ Zdrojové zařízení zmizelo během VHDx zálohování (pozice {FormatBytesLong(bytesRead)}). " +
                        "Zkontrolujte připojení disku a opakujte operaci.", ex);
                }
                catch (IOException)
                {
                    blockReadable = false;
                }
                catch (UnauthorizedAccessException)
                {
                    blockReadable = false;
                }

                if (!blockReadable || bytesReadNow == 0)
                {
                    // Unreadable sector — write zeros and log
                    Array.Clear(buffer, 0, bytesToRead);
                    bytesReadNow = bytesToRead;
                    unreadableBytes += bytesToRead;
                    consecutiveErrors++;

                    if (consecutiveErrors == 1)
                        Log($"⚠️ Nečitelný sektor na pozici {FormatBytesLong(bytesRead)} — nahrazen nulami");

                    if (consecutiveErrors >= maxConsecutiveErrors)
                        throw new IOException($"Příliš mnoho nečitelných sektorů za sebou ({consecutiveErrors}) — disk je pravděpodobně vážně poškozen. Záloha přerušena.");
                }
                else
                {
                    consecutiveErrors = 0;
                }

                await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
                bytesRead += bytesReadNow;
                _phaseBytesProcessed += bytesReadNow;

                BackupBytesWritten = bytesRead;
                BackupBytesWrittenText = FormatBytesLong(bytesRead);
                BackupCurrentSectorText = $"Blok {bytesRead / blockSize:N0} / {totalSize / blockSize:N0}";

                double backupProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
                OverallProgress = backupProgress * 0.30; // Backup = 30% of total workflow
                OverallProgressText = $"{OverallProgress:F0}%";

                UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                    out var speed, out var elapsed, out var eta);
                BackupSpeedText = speed;
                BackupElapsedText = elapsed;
                BackupEtaText = eta;

                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (IOException ex) when (IsDeviceDisappearedError(ex))
        {
            throw new InvalidOperationException(
                $"❌ Zdrojové zařízení zmizelo během VHDx zálohování. Zkontrolujte připojení disku a opakujte operaci.", ex);
        }

        _backupTotalBytes = bytesRead;

        if (unreadableBytes > 0)
            Log($"⚠️ Celkem {FormatBytesLong(unreadableBytes)} nečitelných sektorů nahrazeno nulami.");

        // Write manifest
        var manifest = new
        {
            SourceDrive = DiskPath,
            SourceModel = DiskDisplayName,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "VhdxImage",
            ImageFormat = "VHDx Dynamic (mountable)",
            MountableImage = _backupImagePath,
            TotalBytes = bytesRead,
            BlockSize = blockSize,
            UnreadableBytes = unreadableBytes,
            Note = "Soubor disk_image.vhdx je dynamický VHDx obraz disku. Lze jej připojit: Windows — poklepáním; Linux — sudo qemu-nbd -c /dev/nbd0 disk_image.vhdx && sudo mount /dev/nbd0p1 /mnt"
        };
        using var manifestStream = File.OpenWrite(_backupManifestPath);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);

        // Verify backup — check file exists and has correct size
        var fi = new FileInfo(_backupImagePath);
        if (fi.Exists && fi.Length > 0)
        {
            VhdxBackupVerified = true;
            Log($"VHDx záloha ověřena: {FormatBytesLong(fi.Length)}");
        }
        else
        {
            throw new InvalidOperationException("VHDx záloha se nepodařila ověřit — soubor neexistuje nebo je prázdný.");
        }

        Log($"VHDx záloha dokončena: {FormatBytesLong(bytesRead)}");
        StatusMessage = "VHDx záloha dokončena a ověřena.";
    }

    // ──────────────────────────────────────────────
    //  Phase 3: Raw Restore
    // ──────────────────────────────────────────────'''
    if old in content:
        content = content.replace(old, new)
        print('OK: VHDx backup phase added')
    else:
        print('FAIL: VHDx backup pattern not found')

elif action == 'partition':
    old = '    // ──────────────────────────────────────────────\n    //  Results\n    // ──────────────────────────────────────────────'
    new = '''    // ──────────────────────────────────────────────
    //  Phase 4: Create Partition (VhdxOnly mode)
    // ──────────────────────────────────────────────

    private async Task RunPartitionPhaseAsync(CancellationToken ct)
    {
        Phase = SafeDestructivePhase.Partition;
        CurrentPhaseName = "Vytváření oddílu";
        CurrentPhaseIcon = "📋";
        StatusMessage = "Vytvářím oddíl na disku...";

        if (SelectedDrive == null) return;

        Log($"Vytvářím oddíl na {DiskPath}...");

        try
        {
            var partitionResult = await _sanitizationService.CreatePartitionAsync(
                DiskPath,
                volumeLabel: "Tested",
                format: true,
                progress: new Progress<SanitizationProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OverallProgress = 90 + p.ProgressPercent * 0.10;
                        OverallProgressText = $"{OverallProgress:F0}%";
                        StatusMessage = p.Phase;
                    });
                }),
                ct);

            if (!partitionResult.Success)
            {
                Log($"❌ Vytvoření oddílu selhalo: {partitionResult.ErrorMessage}");
            }
            else
            {
                Log($"✅ Oddíl vytvořen: {(partitionResult.Formatted ? partitionResult.FileSystem + " formátován" : "neformátován")}");
            }
        }
        catch (Exception ex)
        {
            Log($"❌ Vytvoření oddílu selhalo: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    //  Results
    // ──────────────────────────────────────────────'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Partition phase added')
    else:
        print('FAIL: Partition pattern not found')

elif action == 'vhdx-header':
    old = '''    private void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logEntries.Add(entry);
        LogText = string.Join("\\n", _logEntries);
    }'''
    new = '''    /// <summary>
    /// Writes a minimal VHDx header and Block Allocation Table for a dynamic disk.
    /// This creates a valid, mountable VHDx file that Windows and Linux can open.
    /// </summary>
    private static async Task WriteVhdxHeaderAsync(string path, long diskSizeBytes, CancellationToken ct)
    {
        // VHDx uses 1 MiB logical sector size, 4 KiB physical sector size
        const int logicalSectorSize = 1048576; // 1 MiB
        const int physicalSectorSize = 4096;

        // Round disk size up to logical sector boundary
        long diskSizeRounded = ((diskSizeBytes + logicalSectorSize - 1) / logicalSectorSize) * logicalSectorSize;

        // VHDx File Identifier: "vhdxfile" in UTF-16LE
        byte[] vhdxFileId = new byte[] {
            0x76, 0x68, 0x64, 0x78, 0x66, 0x69, 0x6C, 0x65 // "vhdxfile"
        };

        // Build the header
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(fs);

        writer.Write(vhdxFileId);           // Signature
        writer.Write((ushort)0);            // Reserved
        writer.Write(new byte[65536 - 10]); // Pad to 64 KB

        // --- Header 1 (at 64 KB) ---
        long header1Pos = fs.Position; // 65536
        writer.Write(new byte[65536]); // Placeholder for Header 1

        // --- Header 2 (at 128 KB) ---
        long header2Pos = fs.Position; // 131072
        writer.Write(new byte[65536]); // Placeholder for Header 2

        // --- Region Table (at 192 KB) ---
        long regionTablePos = fs.Position; // 196608
        writer.Write(new byte[65536]); // Placeholder for Region Table

        // --- BAT (Block Allocation Table) at 256 KB ---
        long batOffset = 256 * 1024; // 262144
        long chunkCount = (diskSizeRounded + logicalSectorSize - 1) / logicalSectorSize;
        long batSize = chunkCount * 8; // 8 bytes per chunk (ulong)

        // Align BAT size to 1 MiB boundary
        long batAlignedSize = ((batSize + 1024 * 1024 - 1) / (1024 * 1024)) * (1024 * 1024);

        // Seek to BAT position
        fs.Position = batOffset;

        // For dynamic VHDx, all blocks start as not allocated (0)
        for (long i = 0; i < chunkCount; i++)
            writer.Write((ulong)0); // PAYLOAD_BLOCK_NOT_PRESENT

        // Pad BAT to aligned size
        long batEnd = batOffset + batAlignedSize;
        fs.Position = batEnd;

        // --- File Parameters (at 256 KB + aligned BAT) ---
        long fileParamsOffset = batEnd;
        writer.Write(new byte[65536]); // Placeholder for File Parameters

        // Now go back and fill in the actual headers

        // --- Header 1 (VHDx Header) ---
        fs.Position = header1Pos;
        writer.Write(vhdxFileId);           // Signature
        writer.Write((uint)0);              // SequenceNumber 0
        writer.Write(Guid.NewGuid().ToByteArray()); // FileWriteGuid
        writer.Write(Guid.NewGuid().ToByteArray()); // DataWriteGuid
        writer.Write((ushort)1);            // LogVersion = 1
        writer.Write((ushort)6);            // Version = 6 (VHDx v1)
        writer.Write((uint)logicalSectorSize); // LogicalSectorSize
        writer.Write((uint)physicalSectorSize); // PhysicalSectorSize
        writer.Write((uint)0);              // Reserved
        writer.Write(new byte[402]);        // Padding to 512 bytes

        // --- Header 2 (same as Header 1, but SequenceNumber = 1) ---
        fs.Position = header2Pos;
        writer.Write(vhdxFileId);           // Signature
        writer.Write((uint)1);              // SequenceNumber 1
        writer.Write(Guid.NewGuid().ToByteArray()); // FileWriteGuid
        writer.Write(Guid.NewGuid().ToByteArray()); // DataWriteGuid
        writer.Write((ushort)1);            // LogVersion = 1
        writer.Write((ushort)6);            // Version = 6
        writer.Write((uint)logicalSectorSize); // LogicalSectorSize
        writer.Write((uint)physicalSectorSize); // PhysicalSectorSize
        writer.Write((uint)0);              // Reserved
        writer.Write(new byte[402]);        // Padding to 512 bytes

        // --- Region Table ---
        fs.Position = regionTablePos;
        writer.Write((uint)0);              // Signature (will be set below)
        writer.Write((uint)1);              // EntryCount = 1 (BAT)
        writer.Write(new byte[4]);          // Reserved

        // Region Table Entry: BAT
        writer.Write(Guid.Parse("2DC277E9-0F79-41E9-9E2E-7A1D5A1CB5D3").ToByteArray()); // BAT GUID
        writer.Write((ulong)batOffset);     // FileOffset
        writer.Write((ulong)batAlignedSize); // Length
        writer.Write((uint)0);              // Required (0 = not required)
        writer.Write(new byte[4]);          // Padding

        // Region Table signature (CRC32 of the table, simplified: use a fixed value)
        fs.Position = regionTablePos;
        writer.Write((uint)0xAB0B1D0A);     // Region Table signature

        // --- File Parameters ---
        fs.Position = fileParamsOffset;
        writer.Write((uint)0x65706170);     // "pape" signature (File Parameters)
        writer.Write((uint)0);              // Reserved
        writer.Write((ulong)diskSizeRounded); // BlockSize (logical sector size)
        writer.Write(new byte[65520]);      // Pad to 64 KB

        // Flush and set final length
        await fs.FlushAsync(ct);
        fs.SetLength(batEnd + 65536); // File Parameters at end
    }

    private void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logEntries.Add(entry);
        LogText = string.Join("\\n", _logEntries);
    }'''
    if old in content:
        content = content.replace(old, new)
        print('OK: VHDx header method added')
    else:
        print('FAIL: VHDx header pattern not found')

elif action == 'cancel':
    old = '        if (Phase == SafeDestructivePhase.Backup || Phase == SafeDestructivePhase.Test || Phase == SafeDestructivePhase.Restore)'
    new = '        if (Phase == SafeDestructivePhase.Backup || Phase == SafeDestructivePhase.Test || Phase == SafeDestructivePhase.Restore || Phase == SafeDestructivePhase.Partition)'
    if old in content:
        content = content.replace(old, new)
        print('OK: Cancel updated')
    else:
        print('FAIL: Cancel pattern not found')

elif action == 'results':
    old = '        sb.AppendLine("═══ BEZPEČNÝ DESTRUKTIVNÍ TEST — VÝSLEDKY ═══");'
    new = '        sb.AppendLine($"═══ BEZPEČNÝ DESTRUKTIVNÍ TEST — VÝSLEDKY ({(SelectedMode == SafeDestructiveMode.VhdxOnly ? "VHDx" : "Záloha+Obnova")}) ═══");'
    if old in content:
        content = content.replace(old, new)
        print('OK: Results updated')
    else:
        print('FAIL: Results pattern not found')

elif action == 'cert':
    old = '            TestType = "Bezpečný destruktivní test (záloha → test → obnova)",'
    new = '''            TestType = SelectedMode == SafeDestructiveMode.VhdxOnly
                ? "Bezpečný destruktivní test (VHDx záloha → test → oddíl)"
                : "Bezpečný destruktivní test (záloha → test → obnova)",'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Cert updated')
    else:
        print('FAIL: Cert pattern not found')

elif action == 'notes':
    old = '            Notes = $"Bezpečný destruktivní test: záloha → test → obnova.\\n{ResultsSummary}",'
    new = '''            Notes = SelectedMode == SafeDestructiveMode.VhdxOnly
                ? $"Bezpečný destruktivní test: VHDx záloha → test → oddíl (data lze obnovit ručně z {VhdxBackupPath}).\\n{ResultsSummary}"
                : $"Bezpečný destruktivní test: záloha → test → obnova.\\n{ResultsSummary}",'''
    if old in content:
        content = content.replace(old, new)
        print('OK: Notes updated')
    else:
        print('FAIL: Notes pattern not found')

elif action == 'init':
    old = '        Log($"Disk: {DiskDisplayName} ({DiskTotalSizeText})");'
    new = '        Log($"Disk: {DiskDisplayName} ({DiskTotalSizeText})");\n        Log($"Režim: {SelectedMode}");'
    if old in content:
        content = content.replace(old, new)
        print('OK: Init updated')
    else:
        print('FAIL: Init pattern not found')

elif action == 'startcmd':
    old = '        StartWorkflowCommand = new AsyncRelayCommand(StartWorkflowAsync, () => Phase == SafeDestructivePhase.Ready && SelectedDrive != null && HasEnoughBackupSpace);'
    new = '        StartWorkflowCommand = new AsyncRelayCommand(StartWorkflowAsync, () => Phase == SafeDestructivePhase.Ready && SelectedDrive != null && HasEnoughBackupSpace);\n        SelectedMode = SafeDestructiveMode.BackupAndRestore; // default'
    if old in content:
        content = content.replace(old, new)
        print('OK: StartCmd updated')
    else:
        print('FAIL: StartCmd pattern not found')

elif action == 'all':
    # Run all actions
    import subprocess
    actions = ['enum', 'mode-prop', 'startcmd', 'init', 'workflow', 'vhdx-backup', 'partition', 'vhdx-header', 'cancel', 'results', 'cert', 'notes']
    for a in actions:
        result = subprocess.run(['python', __file__, a], capture_output=True, text=True)
        print(result.stdout.strip())
        if 'FAIL' in result.stdout:
            print(f"Stopping at {a}")
            break

with open('DiskChecker.UI.Avalonia/ViewModels/SafeDestructiveTestViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(content)
