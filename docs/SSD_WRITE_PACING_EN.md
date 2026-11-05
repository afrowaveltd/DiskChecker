# Poor SSD Drive Sanity — optional write pacing

DiskChecker offers **optional pauses during the destructive sanitization write pass**.
This is useful for experimenting with SSDs whose sustained writes become slow when
their pseudo-SLC cache or background garbage collection is stressed.

At the existing destructive-confirmation flow, choose a **pause in whole seconds**:

- **0** (default): the original uninterrupted sanitization (no pacing).
- **1–60**: after **each 10 GiB actually written**, wait that number of seconds,
  except at the end of the write pass.
- The read/verify pass and optional format/partition steps **never pause**.

The user-selected mode affects **write phase only**. It still destroys all data,
writes zeros, reads/verifies them, and may recreate/format partitions as before.
Do not use without backup and explicit target-disk verification.

## Measurement semantics

The write graph plots measured chunk throughput during active writes. Deliberate
pauses are announced as an idle state and are *not* plotted as stalled I/O.
The average active write speed and `WriteDuration` exclude deliberate pauses.
`Duration` (the total sanitization elapsed time) includes them. Read speed is
unaffected. The saved session notes include total paused seconds, number of pauses,
active-write speed, and effective wall-time write throughput including pauses.

Idle time is not an SSD `FLUSH` command; the tool **does not claim** the
device's SLC cache is actually emptied after each pause. The best comparison
is the same drive, same capacity, comparable temperature and conditions,
with 0 s / 1 s / 5 s settings.

## Implementation and tests

`SanitizationWritePacingController` defines precise 10-GiB boundaries and
active-time accounting. Linux's direct-I/O and fallback paths and Windows's
raw-write path share the controller. Read/verify code is intentionally unchanged.
Old `SanitizeDiskAsync` calls retain uninterrupted default behavior; paced
calls use the separate opt-in `SanitizeDiskPacedAsync` entry point.

Run:

```bash
dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj -c Release
dotnet test tests/DiskChecker.Tui.Tests/DiskChecker.Tui.Tests.csproj -c Release
```

Tested against a deterministic pacing controller without touching real hardware.
Physical-drive integration on both Windows and Linux remains required.
