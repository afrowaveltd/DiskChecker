$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# ── 1. Replace LoadSpeedSeriesDownsampledAsync ──
$oldSpeedDownsample = @'
    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu vzorků z jedné tabulky pomocí
    /// window funkce <c>ROW_NUMBER()</c>.  Funguje na SQLite >= 3.25.
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
        var orderClause = hasTimestampColumn ? "Timestamp, Id" : "Id";

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, {stalledSelect}
            FROM (
                SELECT
                    ProgressPercent,
                    SpeedMBps,
                    {timestampSelect},
                    {bytesSelect},
                    {stalledSelect},
                    ROW_NUMBER() OVER (ORDER BY {orderClause}) AS _rn,
                    COUNT(*) OVER () AS _total
                FROM {tableName}
                WHERE TestSessionId = @sessionId
                  AND (SpeedMBps > 0 OR {stalledSelect} <> 0)
            )
            WHERE _total <= @maxPoints OR
                  ((_rn - 1) * @maxPoints / _total) <> (((_rn - 2) * @maxPoints) / _total)
            ORDER BY _rn
            LIMIT @maxPoints";

        var sessionParam = command.CreateParameter();
        sessionParam.ParameterName = "@sessionId";
        sessionParam.Value = sessionId;
        command.Parameters.Add(sessionParam);

        var maxPointsParam = command.CreateParameter();
        maxPointsParam.ParameterName = "@maxPoints";
        maxPointsParam.Value = maxPoints;
        command.Parameters.Add(maxPointsParam);

        return await ReadSpeedSamplesAsync(command);
    }
'@

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
'@

# ── 2. Replace GetTemperatureSampleSeriesDownsampledAsync ──
$oldTempDownsample = @'
    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu teplotních vzorků s limitem
    /// <paramref name="maxPoints"/> záznamů v paměti.
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
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var hasTimestampColumn = await ColumnExistsAsync(connection, "TestSessions_TemperatureSamples", "Timestamp");

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT Timestamp, TemperatureCelsius, Phase, ProgressPercent
                FROM (
                    SELECT
                        Timestamp,
                        TemperatureCelsius,
                        Phase,
                        ProgressPercent,
                        ROW_NUMBER() OVER (ORDER BY {(hasTimestampColumn ? "Timestamp, " : "")}Id) AS _rn,
                        COUNT(*) OVER () AS _total
                    FROM TestSessions_TemperatureSamples
                    WHERE TestSessionId = @sessionId
                )
                WHERE _total <= @maxPoints OR
                      ((_rn - 1) * @maxPoints / _total) <> (((_rn - 2) * @maxPoints) / _total)
                ORDER BY _rn
                LIMIT @maxPoints";

            var sessionParam = command.CreateParameter();
            sessionParam.ParameterName = "@sessionId";
            sessionParam.Value = sessionId;
            command.Parameters.Add(sessionParam);

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
            var countParam = countCmd.CreateParameter();
            countParam.ParameterName = "@sessionId";
            countParam.Value = sessionId;
            countCmd.Parameters.Add(countParam);

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
'@

# ── 3. Replace ConfigureSqliteReadOptimizationsAsync ──
$oldConfig = @'
    private static async Task ConfigureSqliteReadOptimizationsAsync(DbConnection connection)
    {
        await using var pragmaCommand = connection.CreateCommand();
        // IMPORTANT: Do NOT use temp_store=MEMORY here. Window-function downsampling
        // (ROW_NUMBER + COUNT OVER) forces SQLite to sort millions of rows. With
        // temp_store=MEMORY the entire sort spills into the application's virtual
        // memory, which can exhaust all system RAM on large sanitization tests
        // (500GB+ drives) and crash the OS. Default temp_store=FILE allows SQLite
        // to use temporary disk files for large intermediate results.
        pragmaCommand.CommandText = "PRAGMA cache_size=-20000;";
        await pragmaCommand.ExecuteNonQueryAsync();
    }
'@

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
'@

# ── Apply replacements ──
$before = $content
$content = $content.Replace($oldSpeedDownsample, $newSpeedDownsample)
Write-Output ("Speed downsample replaced: " + ($content -ne $before))

$before2 = $content
$content = $content.Replace($oldTempDownsample, $newTempDownsample)
Write-Output ("Temp downsample replaced: " + ($content -ne $before2))

$before3 = $content
$content = $content.Replace($oldConfig, $newConfig)
Write-Output ("Config replaced: " + ($content -ne $before3))

# Also remove ConfigureSqliteReadOptimizationsAsync call from GetSpeedSampleSeriesDownsampledAsync
$before4 = $content
$content = $content.Replace("await ConfigureSqliteReadOptimizationsAsync(connection);`r`n`r`n            var writeSamples", "var writeSamples")
$content = $content.Replace("await ConfigureSqliteReadOptimizationsAsync(connection);`r`n            var writeSamples", "var writeSamples")
Write-Output ("Stall info ConfigureSqlite call: " + ($content -ne $before4))

# Write back
[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
Write-Output ("File written. Final length: " + (Get-Content $file -Encoding UTF8 | Measure-Object -Line).Lines)
