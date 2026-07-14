$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.Collections.ArrayList]@([System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8))

# Helper: split a multi-line string into an array of lines
function ToLines($text) {
    return $text -split '\r?\n'
}

# ── New LoadSpeedSeriesDownsampledAsync ──
$newSpeedDownsample = ToLines @'
    /// <summary>
    /// Loads evenly distributed subset of samples using Id-modulo sampling.
    /// Does NOT use window functions (ROW_NUMBER/COUNT OVER), so SQLite
    /// does not create temp files for sorting millions of rows.
    /// This was the main cause of disk exhaustion on large sanitization tests.
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

        // Get total count first (cheap with index)
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

        // Small dataset: read all directly
        if (totalCount <= maxPoints)
        {
            return await LoadSpeedSeriesAsync(connection, tableName, sessionId);
        }

        // Large dataset: modulo sampling on Id (no window function = no temp files)
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
'@

# ── New GetTemperatureSampleSeriesDownsampledAsync ──
$newTempDownsample = ToLines @'
    /// <summary>
    /// Loads evenly distributed temperature samples using Id-modulo sampling.
    /// Does NOT use window functions.
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

            // Get total count (cheap with index)
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
                // Small dataset: read all directly
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

            // Large dataset: modulo sampling (no window function)
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
'@

# ── New ConfigureSqliteReadOptimizationsAsync ──
$newConfig = ToLines @'
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
'@

Write-Output "SpeedDownsample lines: $($newSpeedDownsample.Count)"
Write-Output "TempDownsample lines: $($newTempDownsample.Count)"
Write-Output "Config lines: $($newConfig.Count)"

# Find method boundaries (original file)
$tempStart = -1; $speedStart = -1; $configStart = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match "public async Task.*GetTemperatureSampleSeriesDownsampledAsync" -and $tempStart -lt 0) { $tempStart = $i - 1 }
    if ($l -match "private static async Task.*LoadSpeedSeriesDownsampledAsync" -and $speedStart -lt 0) { $speedStart = $i - 1 }
    if ($l -match "private static async Task ConfigureSqliteReadOptimizationsAsync" -and $configStart -lt 0) { $configStart = $i }
}

# Find ends
function Find-End($s) {
    $d = 0; $started = $false
    for ($i = $s; $i -lt $lines.Count; $i++) {
        $ln = $lines[$i]
        $opens = ($ln.ToCharArray() | Where-Object {$_ -eq '{'}).Count
        $closes = ($ln.ToCharArray() | Where-Object {$_ -eq '}'}).Count
        $d += $opens; $d -= $closes
        if ($opens -gt 0) { $started = $true }
        if ($started -and $d -eq 0) { return $i }
    }
    return $lines.Count - 1
}

$tempEnd = Find-End ($tempStart + 2)
$speedEnd = Find-End ($speedStart + 2)
$configEnd = Find-End ($configStart)

Write-Output "TempDownsample: $tempStart - $tempEnd (len=$($tempEnd - $tempStart + 1))"
Write-Output "SpeedDownsample: $speedStart - $speedEnd (len=$($speedEnd - $speedStart + 1))"
Write-Output "Configure: $configStart - $configEnd (len=$($configEnd - $configStart + 1))"

# Remove OLD sections (BOTTOM to TOP to preserve indices)
for ($i = 0; $i -lt ($configEnd - $configStart + 1); $i++) { $lines.RemoveAt($configStart) }
for ($i = 0; $i -lt ($speedEnd - $speedStart + 1); $i++) { $lines.RemoveAt($speedStart) }
for ($i = 0; $i -lt ($tempEnd - $tempStart + 1); $i++) { $lines.RemoveAt($tempStart) }

Write-Output "After removal: $($lines.Count)"

# Insert NEW sections (TOP to BOTTOM)
# First: TempDownsample at tempStart
for ($i = $newTempDownsample.Count - 1; $i -ge 0; $i--) { $lines.Insert($tempStart, $newTempDownsample[$i]) }
$offset1 = $newTempDownsample.Count - ($tempEnd - $tempStart + 1)
$speedPos = $speedStart + $offset1
Write-Output "Speed insert pos: $speedPos (was $speedStart, offset $offset1)"

# Second: SpeedDownsample at adjusted position
for ($i = $newSpeedDownsample.Count - 1; $i -ge 0; $i--) { $lines.Insert($speedPos, $newSpeedDownsample[$i]) }
$offset2 = $newSpeedDownsample.Count - ($speedEnd - $speedStart + 1)
$configPos = $configStart + $offset1 + $offset2
Write-Output "Config insert pos: $configPos (was $configStart, offsets $offset1 + $offset2)"

# Third: Configure at adjusted position 
for ($i = $newConfig.Count - 1; $i -ge 0; $i--) { $lines.Insert($configPos, $newConfig[$i]) }

Write-Output "After insert: $($lines.Count)"

# Remove unused ConfigureSqliteReadOptimizationsAsync calls
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "await ConfigureSqliteReadOptimizationsAsync\(connection\);" -and $lines[$i] -notmatch "^[\s]*//") {
        $lines[$i] = $lines[$i] -replace "await ConfigureSqliteReadOptimizationsAsync\(connection\);", "            // Not needed: modulo sampling avoids window functions"
        Write-Output "Commented out ConfigureSqlite call at line $i"
    }
}

# Write back
[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Output "Done! Written $($lines.Count) lines"

# Verify
$v = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
Write-Output "Has ROW_NUMBER: $($v.Contains('ROW_NUMBER()'))"
Write-Output "Has modulo: $($v.Contains('Id % @step'))"
Write-Output "Has temp_store: $($v.Contains('temp_store=FILE'))"
