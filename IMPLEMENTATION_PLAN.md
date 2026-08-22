# Implementation Plan: Fix Seagate reallocated-sector count (ID 5 vs ID 196)

## Goal
On Seagate (and other) drives, the "přemapované sektory" (reallocated sectors) value is
wrong: it shows the RAW *event* count instead of the actual reallocated sector count.

- **ID 5** = `Reallocated_Sector_Ct` → the real number of reallocated sectors (what we want).
- **ID 196** = `Reallocated_Event_Count` → number of reallocation *events*; this is
  non-zero even on healthy drives and must NOT be used as the reallocated-sector count.

## Root cause
Two places match the reallocated counter by the broad name fragment `"Reallocated"`,
which also matches `Reallocated_Event_Count` (ID 196):

1. `DiskChecker.UI.Avalonia/ViewModels/SmartCheckViewModel.cs`
   - `MergeCriticalCountersFromAttributes` → `FindRawCounter(attrs, 5, "Reallocated")`.
   - `FindRawCounter` filters `a.Id == id || a.Name.Contains(nameFragment)` and then
     `OrderByDescending(a => a.RawValue).First()`. Because ID 196's raw value is usually
     larger than ID 5's, it picks ID 196 and overwrites the correct value.
2. `DiskChecker.Infrastructure/Hardware/WindowsSmartJsonParser.cs`
   - `PopulateAttributes` uses `id == 5 || name.Contains("Reallocated")`, so a later
     ID 196 entry overwrites the ID 5 value.

## Fix
- `FindRawCounter`: prefer an **exact ID match** first; only fall back to name matching
  when no attribute with the exact ID exists. Also narrow the reallocated name fragment
  from `"Reallocated"` to `"Reallocated_Sector"` so the fallback never matches ID 196.
- `WindowsSmartJsonParser.PopulateAttributes`: match `id == 5` or
  `name.Contains("Reallocated_Sector")` (not the generic `"Reallocated"`).

## Progress
- [x] Analyze root cause
- [x] Fix SmartCheckViewModel.FindRawCounter / MergeCriticalCountersFromAttributes
- [x] Fix WindowsSmartJsonParser.PopulateAttributes
- [x] Add regression tests (ReallocatedSectorDetectionTests.cs, 4 tests)
- [x] Build & verify (0 errors)

## Verification
- `dotnet build` → 0 errors.
- New tests pass: `ReallocatedSectorDetectionTests` (4/4).
- Full suite: 329 total, 25 failed — all failures are pre-existing
  `CertificateGenerator`/`CertificateExportService` tests failing with
  `UnauthorizedAccessException` on `/home/sa-admin/.config/DiskChecker/Certificates`
  (environment permission issue, unrelated to this change).
