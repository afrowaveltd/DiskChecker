# SQLITE_FULL (SQLite error 13) after physical disk sanitization

## Scope and important distinction

The Windows Avalonia DiskChecker interface can complete a full
raw-device write and full read/verify but then fail during its
**separate** SQLite/database/certificate persistence stage.
An example occurred after a 465.76-GB drive was fully processed:
the UI displayed `SQLite Error 13: database or disk is full`
after both progress bars reached 100%.

**This message does not prove the sanitized drive is full or
damaged.** DiskChecker may be using a database stored in an
entirely different location. The same generic error in the
previous application version could also arise during later
certificate or PDF creation after the authoritative test
session had already been committed successfully.

The storage settings implementation selects an explicit
configured SQLite database first. When no DB path is
configured, it can use a pre-existing `DiskChecker.db`
in the **startup working directory**, then beside the
executable, otherwise in the per-user application data folder.
The database path is pinned at application startup; changing
the setting during the session does **not** relocate the
already-open database.

## Preserve the currently running test first

1. **Do not restart the completed physical test.**
2. Keep the current DiskChecker process open until you have
   inspected the saved results and copied telemetry.
3. **Do not delete or rename a live `DiskChecker.db-wal`,
   `DiskChecker.db-shm` or `pending_*.dat`.** Do not run
   `VACUUM` while the current session is still open.
4. On the current Windows user, open a **new PowerShell
   window** and collect the pending telemetry without
   touching the raw disk or the database:

~~~powershell
$source = Join-Path $env:LOCALAPPDATA 'DiskChecker\Telemetry'
$backup = Join-Path $env:USERPROFILE 'Desktop\DiskChecker-Recovery'
New-Item -Path $backup -ItemType Directory -Force | Out-Null

if (Test-Path -LiteralPath $source) {
    $pending = @(Get-ChildItem -LiteralPath $source -Filter 'pending_*.dat' -File)
    $pending | Sort-Object LastWriteTime -Descending |
        Select-Object Name, Length, LastWriteTime
    $pending | Copy-Item -Destination $backup -ErrorAction Continue
} else {
    Write-Host 'No telemetry folder found for this Windows user.'
}

Get-PSDrive -PSProvider FileSystem | Select-Object Name, Used, Free
~~~

This is an **independent byte-copy of intermediate samples**,
not a valid restored SQLite test record or generated certificate.
If a different elevated Windows account started the app,
its LocalAppData may differ from the console user's.

## Find the actual SQLite location (read-only)

The settings file normally lives at
`%APPDATA%\DiskChecker\settings.json` and contains
`DatabaseProvider` and `DatabaseConnectionString`.
The latter can contain **credentials for other providers**;
do not paste it publicly or into a support ticket without
checking it first. Only share a SQLite database path.

If no explicit DB connection setting was stored, inspect the
current working directory used to start the program,
the executable directory, and the AppData candidate. Do
not assume the configured connection points at C:.

For a concrete database directory, inspect only metadata
and space, not a live copy or a schema modification:

~~~powershell
$dbFolder = 'C:\Path\To\Actual\Database\Folder'
Get-ChildItem -LiteralPath $dbFolder -Filter 'DiskChecker.db*' -File |
    Select-Object Name, Length, LastWriteTime
Get-PSDrive -PSProvider FileSystem |
    Select-Object Name, Used, Free
Get-Item -LiteralPath $env:TEMP |
    Select-Object FullName
~~~

Adjust the example folder to the **actual** configured
SQLite data-source path. With an explicitly configured
custom filename, list that filename together with its
`-wal` and `-shm` companions instead.

`SQLITE_FULL (13)` can result from an actually full
database or journal volume, a per-user quota, SQLite
`max_page_count`, or insufficient working space for
database/temp operations. This report alone cannot
distinguish those cases. Avoid blaming the sanitized disk,
NTFS or SQLite itself without inspecting the relevant
paths and error diagnostics.

## Code correction: accurately classify what survived

Beginning with DiskChecker commit
`c35eca9a95fac6a835cd7a517246879049ebf785`,
the Avalonia test flow explicitly tracks whether
`DiskCardTestService.SaveSanitizationAsync` returned:
the service commits the TestSession record before returning.

- If that method fails, the UI says the test/card
  persistence is not confirmed.
- If that method succeeded but a later certificate
  or PDF step fails, the UI explicitly says the
  **test session is already saved** and warns only
  about the optional output.
- For underlying Microsoft.Data.Sqlite error 13, the
  UI reports the **startup SQLite database path**,
  available disk space on that volume, TEMP path,
  and SQLite extended error code, without printing
  the entire connection string or credentials.
- A persistence failure does **not** automatically
  trigger a destructive disk test retry, data erase,
  SQLite migration or WAL cleanup.
- The application does not yet implement a
  guaranteed self-contained recovery/replay of
  a failed session. Always preserve pending
  telemetry and identify the actual failure source.

The original already-running application cannot
be patched in memory by a GitHub commit. The new
diagnostic behavior is present only when launching
a newly built DiskChecker version. Before assigning
a diagnosis to the specific incident, inspect
the actual SQLite path and storage/volume status.

## Validation

Two regression cases cover the durable-session
versus post-commit artifact-failure status selection;
an additional test checks safe non-SQLite fallback
messages without exposing configured credentials.
The related workflows are:

- https://github.com/afrowaveltd/DiskChecker/actions/runs/45192209701
  — surface/SMART handoff build/tests PASS.
- https://github.com/afrowaveltd/DiskChecker/actions/runs/45192209686
  — Windows/Linux safety tests PASS.

These are application tests, **not a reproduction**
of the exact workstation's SQLite error or a
physical 465-GB disk sanitization.
