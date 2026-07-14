using System;
using System.IO;
using Microsoft.Data.Sqlite;

var dbPath = @"D:\DiskChecker\DiskChecker.UI.Avalonia\bin\Release\net10.0\DiskChecker.db";
if (!File.Exists(dbPath))
{
    Console.WriteLine($"DB not found at {dbPath}");
    dbPath = @"D:\DiskChecker\DiskChecker.db";
    Console.WriteLine($"Trying {dbPath}");
}

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine($"DB: {dbPath} ({new FileInfo(dbPath).Length:N0} bytes)");
Console.WriteLine();

// 1. Find last sanitization session
Console.WriteLine("=== LAST SANITIZATION SESSIONS ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"
        SELECT Id, Name, TestType, StartedAt, CompletedAt,
            WriteSpeedAvgMBps, ReadSpeedAvgMBps,
            (SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = ts.Id) as WriteCount,
            (SELECT COUNT(*) FROM TestSessions_ReadSamples WHERE TestSessionId = ts.Id) as ReadCount,
            (SELECT COUNT(*) FROM TestSessions_TemperatureSamples WHERE TestSessionId = ts.Id) as TempCount
        FROM TestSessions ts
        WHERE TestType = 'Sanitization' OR TestType LIKE '%Sanitiz%'
        ORDER BY Id DESC LIMIT 3";
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

Console.WriteLine();

// 2. Check EXPLAIN QUERY PLAN for the modulo sampling query
Console.WriteLine("=== EXPLAIN QUERY PLAN: Modulo sampling (write samples) ===");
using (var cmd = conn.CreateCommand())
{
    // Use a large step value similar to what would be used for millions of rows
    cmd.CommandText = "EXPLAIN QUERY PLAN SELECT ProgressPercent, SpeedMBps, Timestamp, BytesProcessed, IsStalled FROM TestSessions_WriteSamples WHERE TestSessionId = (SELECT MAX(Id) FROM TestSessions WHERE TestType LIKE '%Sanitiz%') AND SpeedMBps > 0 AND (Id % 5000) = 0 ORDER BY Id LIMIT 512";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
        Console.WriteLine($"  {r.GetInt32(0)}|{r.GetInt32(1)}|{r.GetInt32(2)}|{r.GetString(3)}");
    }
}

Console.WriteLine();

// 3. Check table sizes 
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

Console.WriteLine();

// 4. Check indices
Console.WriteLine("=== INDICES ===");
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT name, tbl_name, sql FROM sqlite_master WHERE type='index' AND tbl_name LIKE '%Speed%' OR tbl_name LIKE '%Temperature%'";
    using var r = cmd.ExecuteReader();
    while (r.Read())
        Console.WriteLine($"  {r.GetString(0)} ON {r.GetString(1)}: {r.GetString(2)}");
}

Console.WriteLine();
Console.WriteLine("=== DONE ===");
