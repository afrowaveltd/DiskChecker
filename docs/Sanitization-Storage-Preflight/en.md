# Storage preflight before destructive sanitization

A complete write-and-verify surface operation can take hours. It is
important to detect failures in **result storage** before beginning
raw disk writes.

## The two independent storage locations

**SQLite** is configured through DiskChecker settings and can be
stored in the working directory, executable directory, user AppData,
or an explicitly configured absolute path. Its transaction/WAL
capacity is unrelated to the capacity of the disk being sanitized.

**Telemetry** uses the process's
`Environment.SpecialFolder.LocalApplicationData/DiskChecker/Telemetry`
folder. The user account under which DiskChecker runs matters.
An empty `pending_*.dat` inventory in a different or non-elevated
Windows user profile does not prove none exist elsewhere.

## Preflight checks

After confirming destructive operation and write-pause pacing, but
*before* the raw disk is opened for writing:

1. SQLite: verify that its volume has at least 128 MiB free,
   the DB is not near its `max_page_count` growth cap,
   and that the real database accepts a one-row write
   inside a transaction, which is **rolled back**. This
   is not a fake connectivity-only check.
2. Telemetry: inventory up to 10,000 pending files,
   distinguish ones older than 24 hours, validate the
   actual folder accepts a 4096-byte file write and
   flush, and verify at least 128 MiB space. The
   temporary write probe is deleted.
3. If either required storage probe fails, show a
   localized error and **do not start sanitization**.
4. If pending files are present, warn the user. Do not
   treat all of them as orphans: another DiskChecker
   instance may be running. They are **never**
   automatically removed by this check.

For PostgreSQL or SQL Server, current preflight proves
reachability only. It is not yet a write transaction
test for these providers.

## Limits

All checks describe conditions **at test start**.
They cannot guarantee that the database or disk will
remain writable after hours of operation. Sudden
disconnects, storage quota changes, WAL growth,
hardware failures or other application processes
can still cause a later save error.

The full SQLite Error 13 investigation and safe
diagnostic steps are in
[SQLite Sanitization Recovery](../Sqlite-Sanitization-Recovery/en.md).
Do not delete live SQLite WAL files, pending telemetry,
or run an automatic destructive retry.

The separate preflight tests use disposable SQLite
and temporary directories. They do not access raw
devices or modify the user's DiskChecker history.
