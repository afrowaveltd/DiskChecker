# Implementation Plan: Seek test certificate shows zero SMART parameters

## Goal
Fix a bug where the PDF certificate generated after a **Seek test** shows zero (N/A) SMART
parameters (Power-on hours, power cycles, reallocated sectors, pending sectors, SMART health),
while the same certificate for a **surface test** shows them correctly.

## Root cause
`SeekTestViewModel.SaveTestSessionAndCertificateAsync` builds the `TestSession` **without**
setting `SmartBefore` (the SMART snapshot). It only serializes the `SeekTestResult` into
`SeekResultsJson`.

`SeekTestService.RunAsync` *does* retrieve the full `SmartaData` (via `_smartaProvider`), but it
only copies `PowerOnHours` into `result.PowerOnHours` and discards the rest of the SMART data.

Consequently, when `CertificateGenerator.GenerateCertificateAsync` runs, `session.SmartBefore`
is `null`, so every SMART-derived field falls back to `diskCard` values or `0`:

```csharp
var powerOnHours = session.SmartBefore?.PowerOnHours ?? diskCard.PowerOnHours ?? 0;
var powerCycles = session.SmartBefore?.PowerCycleCount ?? diskCard.PowerCycleCount ?? 0;
var reallocatedSectors = session.SmartBefore?.ReallocatedSectorCount ?? GetSmartAttributeValue(session, 5) ?? 0;
var pendingSectors = session.SmartBefore?.PendingSectorCount ?? GetSmartAttributeValue(session, 197) ?? 0;
```

Surface tests do not have this problem because `DiskCardTestService.SaveSurfaceTestAsync` /
`SaveSanitizationAsync` set `SmartBefore = smartaData` on the session.

## Fix
Carry the full `SmartaData` snapshot through the seek test result and persist it on the session:

1. Add `SmartaData? SmartaData` to `SeekTestResult` (Core/Models/SeekTestModels.cs).
2. In `SeekTestService.RunAsync`, set `result.SmartaData = smartaData` (alongside the existing
   `result.PowerOnHours`).
3. In `SeekTestViewModel.SaveTestSessionAndCertificateAsync`, set
   `SmartBefore = result.SmartaData` on the `TestSession` (the `TestSession.SmartBefore` setter
   already serializes to `SmartBeforeJson` for persistence, matching the surface-test path).

## Progress
- [x] Add `SmartaData` to `SeekTestResult`
- [x] Populate `result.SmartaData` in `SeekTestService.RunAsync`
- [x] Set `SmartBefore` on the session in `SeekTestViewModel`
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` cannot run in this environment (`Exec format error` on test host exe — pre-existing).
