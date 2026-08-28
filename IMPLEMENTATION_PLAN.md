# Implementation Plan: Historical thermal SMART failure must not force grade F

## Goal
Fix an edge case where a SMART attribute related exclusively to temperature
(e.g. `Airflow_Temperature_Cel` / attribute 190) with `WHEN_FAILED = In_the_past`
alone forces the disk to grade F, even though the current state and full physical
tests are clean.

## Root cause
Three places treat *any* failing SMART attribute (`!IsOk && WhenFailed != ""`) or the
coarse `IsFailing` flag (which the parser also sets for `In_the_past`) as a critical
failure and force grade F / `TestResult.Fail`, without distinguishing a historical
temperature-only threshold crossing from an active media/mechanical failure:

1. `DiskChecker.Core/Services/QualityCalculator.cs`
   - `HasCriticalSmartFailure(...)` returns `true` for
     `smartaData.Attributes.Any(a => !a.IsOk && !string.IsNullOrWhiteSpace(a.WhenFailed))`
     and for `smartaData.IsFailing`.
   - `CalculateQuality(...)` then returns `QualityGrade.F`.

2. `DiskChecker.Application/Services/DiskCardTestService.cs`
   - `HasSmartFailure(...)` uses the same `Attributes.Any(...)` check and, in
     `ApplySmartQuality(...)`, degrades the surface-test grade to `"F"`.

3. `DiskChecker.Application/Services/DiskCardTestService.cs`
   - `DetermineSmartResult(...)` returns `TestResult.Fail` when `IsFailing` is true.

## Fix (minimal, no broad refactor)
Added a narrow helper `QualityCalculator.IsHistoricalTemperatureOnlyFailure(SmartaData)`
(public static) that returns `true` only when:
- overall health is OK (`IsHealthy`),
- there are no active critical media/mechanical indicators (pending, UNC, media
  errors, reallocated > 50, percentage used >= 100, available spare <= 1),
- every failing attribute is a temperature attribute (ID 190 or 194) with
  `WHEN_FAILED = In_the_past` and is not currently below threshold.

Then:
- `HasCriticalSmartFailure` returns `false` for that case (no forced F).
- A new `ApplyHistoricalThermalPenalty` (inline in `CalculateQuality`) subtracts a
  non-negligible 15 points and adds the required explanation text.
- `DiskCardTestService.HasSmartFailure` reuses the helper (no forced F on surface path).
- `DiskCardTestService.DetermineSmartResult` returns `TestResult.Warning` (not `Fail`)
  for the historical thermal case.

Active critical indicators always keep priority (checked before the exemption).

## Progress
- [x] Add helper + penalty to `QualityCalculator`
- [x] Update `DiskCardTestService.HasSmartFailure`
- [x] Update `DiskCardTestService.DetermineSmartResult`
- [x] Add tests (`HistoricalThermalFailureTests.cs`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` cannot run in this environment (`Exec format error` on test host exe — pre-existing).
- Test project compiles cleanly (0 errors).

---

# Implementation Plan: FullStroke seek test score must match grade bands

## Goal
The numeric percentage score for a FullStroke seek test was decoupled from the
letter grade. `CalculateSeekGrade` used absolute latency thresholds, while
`CalculateSeekScore` used a consistency-based formula that returned ~77 for most
disks regardless of grade. Make the score consistent with the A–F grade bands.

## Requested bands
- A = 80–100
- B = 60–80
- C = 40–60
- D = 20–40
- E = 1–20
- F = 0 (failure: aborted, incomplete, errors, or no samples)

## Fix
In `DiskChecker.UI.Avalonia/ViewModels/SeekTestViewModel.cs`:
- Added `CalculateFullStrokeScore(SeekTestResult)` that maps average latency to a
  score within the grade band using linear interpolation:
  - `<15ms` → A (100 down to 80)
  - `<20ms` → B (80 down to 60)
  - `<25ms` → C (60 down to 40)
  - `<30ms` → D (40 down to 20)
  - `>=30ms` → E (20 down to 1)
  - failure → 0
- `CalculateSeekScore` now delegates to `CalculateFullStrokeScore` for
  `SeekTestType.FullStroke`, keeping the non-FullStroke consistency formula unchanged.

## Progress
- [x] Add `CalculateFullStrokeScore`
- [x] Delegate in `CalculateSeekScore` for FullStroke
- [x] Add tests (`FullStroke_Score_MatchesGradeBands`, `..._Failure_IsZero`, `..._NoSamples_IsZero`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` cannot run in this environment (`Exec format error` on test host exe — pre-existing).
- Test project compiles cleanly (0 errors).
