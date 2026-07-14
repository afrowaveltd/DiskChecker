$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.Collections.ArrayList]@([System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8))

# New code sections (as arrays of lines)
$newTempDownsample = @'
    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu teplotních vzorků s limitem
    /// <paramref name="maxPoints"/> záznamů v paměti.  Nepoužívá window
    /// funkce – modulo vzorkování na Id sloupci.
    /// </summary>
    public async Task<List<TemperatureSample>> GetTemperatureSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var hasTimestampColumn = await ColumnExistsAsync(connection, "TestSessions_TemperatureSamples", "Timestamp");

            // Zjisti celkový počet (levné díky indexu)
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM TestSessions_TemperatureSamples WHERE TestSessionId = @sessionId";
            var tcountParam = countCmd.CreateParameter();
            tcountParam.ParameterName = "@sessionId";
            tcountParam.Value = sessionId;
            countCmd.Parameters.Add(tcountParam);

            var obj = await countCmd.ExecuteScalarAsync(cancellationToken);
            var totalCount = obj is long l ? l : Convert.ToInt64(obj ?? 0);

            if (totalCount <= maxPoints)
            {
                // Malý dataset: načti všechno
                await using var directCmd = connection.CreateCommand();
                directCmd.CommandText = $@"
                    SELECT Timestamp, TemperatureCelsius, Phase, ProgressPercent
                    FROM TestSessions_TemperatureSamples
                    WHERE TestSessionId = @sessionId
                    ORDER BY {(hasTimestampColumn ? "Timestamp, " : "")}Id";
                var dp = directCmd.CreateParameter();
                dp.ParameterName = "@sessionId";
                dp.Value = sessionId;
                directCmd.Parameters.Add(dp);

                var directValues = new List<TemperatureSample>(Math.Min(maxPoints, 1024));
                await using var dr = await directCmd.ExecuteReaderAsync(cancellationToken);
                while (await dr.ReadAsync(cancellationToken))
                {
                    directValues.Add(new TemperatureSample
                    {
                        Timestamp = dr.IsDBNull(0) ? DateTime.MinValue : dr.GetDateTime(0),
                        TemperatureCelsius = dr.IsDBNull(1) ? 0 : dr.GetInt32(1),
                        Phase = dr.IsDBNull(2) ? string.Empty : dr.GetString(2),
                        ProgressPercent = dr.IsDBNull(3) ? 0 : dr.GetDouble(3)
                    });
                }
                return directValues;
            }

            // Velký dataset: modulo vzorkování (žádná window funkce)
            var step = (int)Math.Max(1, totalCount / maxPoints);

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT Timestamp, TemperatureCelsius, Phase, ProgressPercent
                FROM TestSessions_TemperatureSamples
                WHERE TestSessionId = @sessionId
                  AND (Id % @step) = 0
                ORDER BY {(hasTimestampColumn ? "Timestamp, " : "")}Id
                LIMIT @maxPoints";

            var sessionParam = command.CreateParameter();
            sessionParam.ParameterName = "@sessionId";
            sessionParam.Value = sessionId;
            command.Parameters.Add(sessionParam);

            var stepParam = command.CreateParameter();
            stepParam.ParameterName = "@step";
            stepParam.Value = step;
            command.Parameters.Add(stepParam);

            var maxPointsParam = command.CreateParameter();
            maxPointsParam.ParameterName = "@maxPoints";
            maxPointsParam.Value = maxPoints;
            command.Parameters.Add(maxPointsParam);

            var values = new List<TemperatureSample>(Math.Min(maxPoints, 1024));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(new TemperatureSample
                {
                    Timestamp = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                    TemperatureCelsius = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    Phase = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ProgressPercent = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
                });
            }

            return values;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
'@ -split "`r`n"

$newSpeedDownsample = @'
    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu vzorků z jedné tabulky pomocí
    /// modulo vzorkování na sloupci Id.  Nepoužívá window funkce, takže
    /// SQLite nemusí tvořit dočasné soubory pro třídění milionů řádků
    /// (hlavní příčina exhaustionu disku při velkých sanitizačních testech).
    /// </summary>
    private static async Task<List<SpeedSample>> LoadSpeedSeriesDownsampledAsync(
        DbConnection connection,
        string tableName,
        int sessionId,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        var hasIsStalledColumn = await ColumnExistsAsync(connection, tableName, "IsStalled");
        var hasTimestampColumn = await ColumnExistsAsync(connection, tableName, "Timestamp");
        var hasBytesProcessedColumn = await ColumnExistsAsync(connection, tableName, "BytesProcessed");
        var timestampSelect = hasTimestampColumn ? "Timestamp" : "NULL AS Timestamp";
        var bytesSelect = hasBytesProcessedColumn ? "BytesProcessed" : "0 AS BytesProcessed";
        var stalledSelect = hasIsStalledColumn ? "IsStalled" : "0 AS IsStalled";

        // Zjisti celkový počet odpovídajících řádků (levné díky indexu)
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $@"
            SELECT COUNT(*)
            FROM {tableName}
            WHERE TestSessionId = @sessionId
              AND (SpeedMBps > 0 OR {stalledSelect} <> 0)";
        var countParam = countCmd.CreateParameter();
        countParam.ParameterName = "@sessionId";
        countParam.Value = sessionId;
        countCmd.Parameters.Add(countParam);

        var obj = await countCmd.ExecuteScalarAsync(cancellationToken);
        var totalCount = obj is long l ? l : Convert.ToInt64(obj ?? 0);

        // Malý dataset: načti všechno přímo
        if (totalCount <= maxPoints)
        {
            return await LoadSpeedSeriesAsync(connection, tableName, sessionId);
        }

        // Velký dataset: modulo vzorkování na Id (žádná window funkce → žádné temp soubory)
        var step = (int)Math.Max(1, totalCount / maxPoints);

        await using var command = connection.CreateCommand();
        command.CommandText = hasIsStalledColumn
            ? $@"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, IsStalled
                FROM {tableName}
                WHERE TestSessionId = @sessionId
                  AND (SpeedMBps > 0 OR IsStalled <> 0)
                  AND (Id % @step) = 0
                ORDER BY Id
                LIMIT @maxPoints"
            : $@"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, 0 AS IsStalled
                FROM {tableName}
                WHERE TestSessionId = @sessionId
                  AND SpeedMBps > 0
                  AND (Id % @step) = 0
                ORDER BY Id
                LIMIT @maxPoints";

        var sessionParam = command.CreateParameter();
        sessionParam.ParameterName = "@sessionId";
        sessionParam.Value = sessionId;
        command.Parameters.Add(sessionParam);

        var stepParam = command.CreateParameter();
        stepParam.ParameterName = "@step";
        stepParam.Value = step;
        command.Parameters.Add(stepParam);

        var maxPointsParam = command.CreateParameter();
        maxPointsParam.ParameterName = "@maxPoints";
        maxPointsParam.Value = maxPoints;
        command.Parameters.Add(maxPointsParam);

        return await ReadSpeedSamplesAsync(command);
    }
'@ -split "`r`n"

$newConfig = @'
    private static async Task ConfigureSqliteReadOptimizationsAsync(DbConnection connection)
    {
        await using var pragmaCommand = connection.CreateCommand();
        // Downsampling queries now use Id-modulo sampling instead of window
        // functions (ROW_NUMBER + COUNT OVER), so SQLite does NOT need to
        // sort millions of rows.  The small cache is still sufficient for
        // the COUNT(*) + simple index scan used by the new approach.
        // We also explicitly set temp_store=FILE as a safety measure to
        // prevent any accidental in-memory spill on edge cases.
        pragmaCommand.CommandText = "PRAGMA cache_size=-50000; PRAGMA temp_store=FILE;";
        await pragmaCommand.ExecuteNonQueryAsync();
    }
'@ -split "`r`n"

# Phase 1: Find exact start line for each section (AFTER removing ConfigureSqlite calls)
# We need to find them because line numbers changed from previous script run
$tempStart = -1
$speedStart = -1
$configStart = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public async Task.*GetTemperatureSampleSeriesDownsampledAsync" -and $tempStart -lt 0) {
        $tempStart = $i - 1  # include XML comment
    }
    if ($lines[$i] -match "private static async Task.*LoadSpeedSeriesDownsampledAsync" -and $speedStart -lt 0) {
        $speedStart = $i - 1
    }
    if ($lines[$i] -match "private static async Task ConfigureSqliteReadOptimizationsAsync" -and $configStart -lt 0) {
        $configStart = $i
    }
}

Write-Output "TempDownsample start: $tempStart"
Write-Output "SpeedDownsample start: $speedStart"
Write-Output "Configure start: $configStart"

# Phase 2: Find ends by tracking braces
function Find-End($startIdx) {
    $d = 0; $s = $false
    for ($i = $startIdx; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $opens = ($line.ToCharArray() | Where-Object {$_ -eq '{'}).Count
        $closes = ($line.ToCharArray() | Where-Object {$_ -eq '}'}).Count
        $d += $opens; $d -= $closes
        if ($opens -gt 0) { $s = $true }
        if ($s -and $d -eq 0) { return $i }
    }
    return $lines.Count - 1
}

$tempEnd = Find-End ($tempStart + 2)
$speedEnd = Find-End ($speedStart + 2)
$configEnd = Find-End ($configStart)

Write-Output "TempDownsample: $tempStart - $tempEnd"
Write-Output "SpeedDownsample: $speedStart - $speedEnd"
Write-Output "Configure: $configStart - $configEnd"

# Phase 3: Remove old sections (BOTTOM to TOP to preserve indices)
$oldTempLen = $tempEnd - $tempStart + 1
$oldSpeedLen = $speedEnd - $speedStart + 1
$oldConfigLen = $configEnd - $configStart + 1

# Remove Configure (bottom-most)
for ($i = 0; $i -lt $oldConfigLen; $i++) { $lines.RemoveAt($configStart) }
Write-Output "Removed $oldConfigLen lines for Configure"

# Remove SpeedDownsample
for ($i = 0; $i -lt $oldSpeedLen; $i++) { $lines.RemoveAt($speedStart) }
Write-Output "Removed $oldSpeedLen lines for SpeedDownsample"

# Remove TempDownsample
for ($i = 0; $i -lt $oldTempLen; $i++) { $lines.RemoveAt($tempStart) }
Write-Output "Removed $oldTempLen lines for TempDownsample"

# Phase 4: Insert new sections (TOP to BOTTOM)
# Insert TempDownsample at tempStart
for ($i = $newTempDownsample.Count - 1; $i -ge 0; $i--) { $lines.Insert($tempStart, $newTempDownsample[$i]) }
Write-Output "Inserted $($newTempDownsample.Count) lines for TempDownsample"

# Insert SpeedDownsample at speedStart (adjusted for temp insertion)
$speedInsertPos = $tempStart + $newTempDownsample.Count + ($speedStart - $tempStart - $oldTempLen)
for ($i = $newSpeedDownsample.Count - 1; $i -ge 0; $i--) { $lines.Insert($speedInsertPos, $newSpeedDownsample[$i]) }
Write-Output "Inserted $($newSpeedDownsample.Count) lines for SpeedDownsample"

# Insert Configure at the end position
$configInsertPos = $speedInsertPos + $newSpeedDownsample.Count + ($configStart - $speedStart - $oldSpeedLen)
for ($i = $newConfig.Count - 1; $i -ge 0; $i--) { $lines.Insert($configInsertPos, $newConfig[$i]) }
Write-Output "Inserted $($newConfig.Count) lines for Configure"

# Phase 5: Remove unused ConfigureSqliteReadOptimizationsAsync calls
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "await ConfigureSqliteReadOptimizationsAsync\(connection\);" -and $lines[$i] -notmatch "^\s*//") {
        Write-Output "Removing ConfigureSqlite call at line $i"
        $lines[$i] = $lines[$i] -replace "await ConfigureSqliteReadOptimizationsAsync\(connection\);", "// ConfigureSqliteReadOptimizationsAsync no longer needed (modulo sampling avoids window functions)"
    }
}

# Write back
[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Output "Done! Lines: $($lines.Count)"

# Verify
$verify = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
Write-Output "Has ROW_NUMBER: $($verify.Contains('ROW_NUMBER()'))"
Write-Output "Has modulo step: $($verify.Contains('Id % @step'))"
Write-Output "Has temp_store: $($verify.Contains('temp_store=FILE'))"
