
// Dump SQLite DB stats using dotnet script
#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"D:\DiskChecker\DiskChecker.db";
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine("=== TABLES ===");
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
using var r = cmd.ExecuteReader();
while (r.Read()) Console.WriteLine($"  {r.GetString(0)}");

Console.WriteLine("\n=== TEST SESSIONS (last 5) ===");
cmd.CommandText = "SELECT Id, StartedAt, Name, DiskModel, SerialNumber, CapacityBytes, TestType, TestResult FROM TestSessions ORDER BY Id DESC LIMIT 5";
using var r2 = cmd.ExecuteReader();
for (int i = 0; i < r2.FieldCount; i++) Console.Write(r2.GetName(i) + " | ");
Console.WriteLine();
while (r2.Read())
{
    for (int i = 0; i < r2.FieldCount; i++)
        Console.Write((r2.IsDBNull(i) ? "NULL" : r2.GetValue(i)?.ToString()) + " | ");
    Console.WriteLine();
}

Console.WriteLine("\n=== SAMPLE COUNTS PER SESSION ===");
cmd.CommandText = @"
    SELECT ts.Id, ts.Name, ts.TestType,
        (SELECT COUNT(*) FROM SpeedSamples WHERE TestSessionId = ts.Id) as SpeedCount,
        (SELECT COUNT(*) FROM TemperatureSamples WHERE TestSessionId = ts.Id) as TempCount
    FROM TestSessions ts ORDER BY ts.Id DESC LIMIT 5";
using var r3 = cmd.ExecuteReader();
while (r3.Read())
    Console.WriteLine($"  Session {r3.GetInt32(0)}: {r3.GetString(1)} ({r3.GetString(2)}) - Speed: {r3.GetInt64(3)}, Temp: {r3.GetInt64(4)}");

Console.WriteLine("\n=== DB FILE SIZE ===");
Console.WriteLine($"  {new FileInfo(dbPath).Length:N0} bytes");
