using System;
using System.IO;
using Microsoft.Data.Sqlite;

var dbPath = @"D:\DiskChecker\DiskChecker.db";
Console.WriteLine($"DB: {dbPath} ({new FileInfo(dbPath).Length:N0} bytes)");
Console.WriteLine();

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// PRAGMAs
Console.WriteLine("=== SQLITE SETTINGS ===");
foreach (var pragma in new[] { "temp_store", "cache_size", "journal_mode", "synchronous", "page_size", "page_count" })
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = $"PRAGMA {pragma}";
    var val = cmd.ExecuteScalar();
    Console.WriteLine($"  {pragma} = {val}");
}

// 0. First, check what columns exist
Console.WriteLine();
Console.WriteLine("=== TestSessions COLUMNS ===");
using (var colsCmd = conn.CreateCommand())
{
    colsCmd.CommandText = "PRAGMA table_info('TestSessions')";
    using var cr = colsCmd.ExecuteReader();
    while (cr.Read())
        Console.WriteLine($"  {cr.GetString(1)} ({cr.GetString(2)})");
}
Console.WriteLine();
Console.WriteLine("=== TestSessions_WriteSamples COLUMNS ===");
using (var colsCmd = conn.CreateCommand())
{
    colsCmd.CommandText = "PRAGMA table_info('TestSessions_WriteSamples')";
    using var cr = colsCmd.ExecuteReader();
    while (cr.Read())
        Console.WriteLine($"  {cr.GetString(1)} ({cr.GetString(2)})");
}

// 1. Find last sanitization session
Console.WriteLine();
Console.WriteLine("=== ALL SESSIONS (last 5) ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
        SELECT Id, TestType, StartedAt,
            (SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = ts.Id) as WriteCount,
            (SELECT COUNT(*) FROM TestSessions_ReadSamples WHERE TestSessionId = ts.Id) as ReadCount,
            (SELECT COUNT(*) FROM TestSessions_TemperatureSamples WHERE TestSessionId = ts.Id) as TempCount
        FROM TestSessions ts
        ORDER BY Id DESC LIMIT 5";
    using var r = cmd.ExecuteReader();
    for (int i = 0; i < r.FieldCount; i++) Console.Write(r.GetName(i) + "\t");
    Console.WriteLine();
    while (r.Read())
    {
        for (int i = 0; i < r.FieldCount; i++)
            Console.Write((r.IsDBNull(i) ? "NULL" : r.GetValue(i)) + "\t");
        Console.WriteLine();
    }
}

// 2. Total table counts
Console.WriteLine();
Console.WriteLine("=== TABLE ROW COUNTS ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
        SELECT 'TestSessions' as Tbl, COUNT(*) FROM TestSessions
        UNION ALL SELECT 'WriteSamples', COUNT(*) FROM TestSessions_WriteSamples
        UNION ALL SELECT 'ReadSamples', COUNT(*) FROM TestSessions_ReadSamples  
        UNION ALL SELECT 'TempSamples', COUNT(*) FROM TestSessions_TemperatureSamples";
    using var r = cmd.ExecuteReader();
    while (r.Read())
        Console.WriteLine($"  {r.GetString(0)}: {r.GetInt64(1):N0}");
}

// 3. Find the session ID with most samples
Console.WriteLine();
Console.WriteLine("=== SESSION WITH MOST SAMPLES ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
        SELECT ts.Id, ts.TestType, ts.StartedAt,
            (SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = ts.Id) as W,
            (SELECT COUNT(*) FROM TestSessions_ReadSamples WHERE TestSessionId = ts.Id) as R,
            (SELECT COUNT(*) FROM TestSessions_TemperatureSamples WHERE TestSessionId = ts.Id) as T
        FROM TestSessions ts
        ORDER BY W DESC LIMIT 3";
    using var r = cmd.ExecuteReader();
    while (r.Read())
        Console.WriteLine($"  Session {r.GetInt32(0)}: {r.GetString(1)} W={r.GetInt64(3):N0} R={r.GetInt64(4):N0} T={r.GetInt64(5):N0}");
}

// 4. EXPLAIN QUERY PLAN for modulo sampling
Console.WriteLine();
Console.WriteLine("=== EXPLAIN: Modulo sampling (write) ===");
using (var cmd = conn.CreateCommand())
{
    // Find max session ID
    cmd.CommandText = "SELECT MAX(Id) FROM TestSessions WHERE TestType LIKE '%Sanitiz%'";
    var maxId = cmd.ExecuteScalar();
    if (maxId is not DBNull && maxId != null)
    {
        var sid = Convert.ToInt32(maxId);
        Console.WriteLine($"  Using sessionId={sid}");

        // First get count
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = {sid}";
        var cnt = countCmd.ExecuteScalar();
        var total = cnt is long l ? l : Convert.ToInt64(cnt ?? 0);
        var step = (int)Math.Max(1, total / 512);
        Console.WriteLine($"  Total write samples: {total:N0}, step: {step:N0}");

        // EXPLAIN
        using var explCmd = conn.CreateCommand();
        explCmd.CommandText = $"EXPLAIN QUERY PLAN SELECT * FROM TestSessions_WriteSamples WHERE TestSessionId = {sid} AND (Id % {step}) = 0 ORDER BY Id LIMIT 512";
        using var r = explCmd.ExecuteReader();
        while (r.Read())
            Console.WriteLine($"  PLAN: {r.GetInt32(0)}|{r.GetInt32(1)}|{r.GetInt32(2)}|{r.GetString(3)}");
    }
    else
    {
        Console.WriteLine("  No sanitization session found! Trying any session...");
        cmd.CommandText = "SELECT MAX(Id) FROM TestSessions";
        maxId = cmd.ExecuteScalar();
        var sid = Convert.ToInt32(maxId ?? 0);
        Console.WriteLine($"  Using sessionId={sid}");

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = {sid}";
        var cnt = countCmd.ExecuteScalar();
        var total = cnt is long l ? l : Convert.ToInt64(cnt ?? 0);
        var step = (int)Math.Max(1, total / 512);
        Console.WriteLine($"  Total write samples: {total:N0}, step: {step:N0}");

        if (total > 0)
        {
            using var explCmd = conn.CreateCommand();
            explCmd.CommandText = $"EXPLAIN QUERY PLAN SELECT * FROM TestSessions_WriteSamples WHERE TestSessionId = {sid} AND (Id % {step}) = 0 ORDER BY Id LIMIT 512";
            using var r = explCmd.ExecuteReader();
            while (r.Read())
                Console.WriteLine($"  PLAN: {r.GetInt32(0)}|{r.GetInt32(1)}|{r.GetInt32(2)}|{r.GetString(3)}");
        }
    }
}

// 5. Check indices
Console.WriteLine();
Console.WriteLine("=== TABLE INDICES ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
        SELECT m.name as idx_name, m.tbl_name, 
               (SELECT GROUP_CONCAT(il.name, ',') FROM pragma_index_info(m.name) il) as columns
        FROM sqlite_master m WHERE m.type='index' AND m.name NOT LIKE 'sqlite_autoindex%'
        ORDER BY m.tbl_name";
    using var r = cmd.ExecuteReader();
    while (r.Read())
        Console.WriteLine($"  {r.GetString(0)} ON {r.GetString(1)} ({r.GetString(2)})");
}

Console.WriteLine();
Console.WriteLine("=== DONE ===");
