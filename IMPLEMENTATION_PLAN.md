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

---

# Implementation Plan: Analysis card — measurement selection wiring

## Goal
The "Analýza" (Analysis) card lists available measurements in a left-hand panel,
but clicking a measurement does nothing. The detail analysis (telemetry charts,
anomalies, stalls, seek, temperature, SMART trends) never loads for the clicked
item because the list buttons do not pass the clicked `TestAnalysisSummary` to the
view model.

## Root cause
In `DiskChecker.UI.Avalonia/Views/AnalysisView.axaml`, the measurement list
`ItemsControl` renders a `Button` per summary whose `Command` is
`LoadSelectedAnalysisCommand` (a parameterless `AsyncRelayCommand`), but:

1. There is no `CommandParameter="{Binding}"`, so the clicked summary is never
   forwarded to the view model.
2. There is no `SelectedItem` binding on the `ItemsControl`, so `SelectedSummary`
   is never set from the UI.
3. `LoadSelectedAnalysisCommand` is parameterless and only re-loads whatever
   `SelectedSummary` already is (which is only ever set programmatically to the
   first item during `LoadWorkspaceAsync`).

As a result, the only measurement that can ever be inspected is the first one,
and clicking any other row has no effect.

## Fix (minimal, backward compatible)
Add a parameterized `SelectSummaryCommand` (`IRelayCommand<TestAnalysisSummary>`)
to `AnalysisViewModel` that sets `SelectedSummary` (whose setter already triggers
`LoadSelectedAnalysisAsync`). Wire it in the XAML with `CommandParameter="{Binding}"`
and add a visual "selected" highlight so the active row is obvious.

No changes to the data service, repository, or models are required — the
`SelectedSummary` setter already performs the load.

## Progress
- [x] Add `SelectSummaryCommand` to `AnalysisViewModel`
- [x] Wire `CommandParameter` + selected highlight in `AnalysisView.axaml`
- [x] Add tests for selection behavior (`AnalysisSelectionTests.cs`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` cannot run via the standard runner in this environment (`Exec format error` on the Windows test host exe — pre-existing).
- Tests run successfully via `dotnet exec .../DiskChecker.Tests.dll`:
  - `AnalysisSelectionTests` → 5/5 passed.
  - Pre-existing `CertificateGeneratorTests` failures are unrelated (permission denied on `/home/sa-admin/.config/DiskChecker/Certificates`).
- Test project compiles cleanly (0 errors).

---

# Implementation Plan: Analysis card — blank charts (culture-invariant point strings)

## Goal
The "Analýza" card showed four blank white chart windows even though the disk
selection and measurement list worked. The chart polylines were empty because the
point strings were formatted with the **current culture** (cs-CZ uses a comma as the
decimal separator), producing ambiguous strings like `123,4,56,7` that
`PointsStringConverter` (which parses with `InvariantCulture` and splits on comma)
could not parse.

## Root cause
Four places built polyline point strings with interpolated `:F1` format specifiers
that honor the current culture:

1. `AnalysisViewModel.BuildTelemetryPolyline` → `ThroughputProgress*Points` / `ThroughputTime*Points`
2. `AnalysisViewModel.BuildSeekPolyline` → `SeekLatencyPoints`
3. `AnalysisViewModel.BuildTemperaturePolyline` → `TemperaturePoints`
4. `SmartTrendService.BuildChartData` → SMART trend `PolylinePoints`

The `CertificateViewModel` already used `FormattableString.Invariant(...)`, which is
why the certificate charts rendered correctly while the analysis charts were blank.

## Fix (minimal, backward compatible)
Wrapped each point-string interpolation in `FormattableString.Invariant(...)` so the
decimal separator is always `.` regardless of the thread culture. No model, service,
or XAML changes were required.

## Progress
- [x] Fix `BuildTelemetryPolyline` (AnalysisViewModel)
- [x] Fix `BuildSeekPolyline` (AnalysisViewModel)
- [x] Fix `BuildTemperaturePolyline` (AnalysisViewModel)
- [x] Fix `BuildChartData` (SmartTrendService)
- [x] Add regression tests (`AnalysisChartCultureTests.cs`, `SmartTrendChartCultureTests.cs`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- Tests run via `dotnet exec .../DiskChecker.Tests.dll`:
  - `AnalysisChartCultureTests` → 3/3 passed.
  - `SmartTrendChartCultureTests` → 1/1 passed.
  - `AnalysisSelectionTests` → 5/5 passed (unchanged).
- Test project compiles cleanly (0 errors).

---

# Implementation Plan: Analysis card — comprehensive data loading & SMART text

## Goal
The "Analýza" card only rendered the Seek test charts; all other test types
(QuickRead, FullRead, FullWrite, SurfaceScan, Sanitization) showed blank charts.
The user wants the card to comprehensively access stored data, show all available
metrics, and add text fields (e.g. for SMART).

## Root cause
`TestAnalysisDataService.GetAnalysisDataAsync` only loaded:
- `TestTelemetrySamples` (only Sanitization sessions 6/18 have these),
- `SeekSamples` (only Seek sessions),
- `TestSessions_TemperatureSamples` (empty for all sessions),
- anomalies/stalls.

It never loaded the **legacy owned speed samples** (`TestSessions_WriteSamples` /
`TestSessions_ReadSamples`), which hold the throughput data for QuickRead/FullRead/
FullWrite/SurfaceScan sessions (4,5,7,8,12,14,16,20). So those charts were empty.

Temperature samples were also empty because per-sample temperature was never
persisted to `TestSessions_TemperatureSamples`; only the session-level
`StartTemperature`/`MaxTemperature`/`AverageTemperature` columns are populated.

## Fix
1. In `GetAnalysisDataAsync`, also load legacy write/read samples via
   `GetSpeedSampleSeriesAsync` and convert them to `TestTelemetrySample` (fallback
   when telemetry is empty), so the existing ViewModel chart logic works unchanged.
2. Synthesize a temperature line from session-level temperature metrics when no
   per-sample temperature data exists.
3. Add comprehensive SMART text fields to the ViewModel and wire them in XAML.

## Progress
- [x] Load legacy write/read samples in `TestAnalysisDataService`
- [x] Synthesize temperature samples from session metrics
- [x] Add SMART snapshot text fields to `AnalysisViewModel`
- [x] Wire SMART text fields in `AnalysisView.axaml`
- [x] Add tests (`TestAnalysisDataServiceTests.cs`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- Tests run via `dotnet exec .../DiskChecker.Tests.dll`:
  - `TestAnalysisDataServiceTests` → 3/3 passed.
  - `AnalysisSelectionTests` → 5/5 passed (unchanged).
  - `AnalysisChartCultureTests` → 3/3 passed (unchanged).
  - `SmartTrendChartCultureTests` → 1/1 passed (unchanged).
- Test project compiles cleanly (0 errors).


---
# Implementation Plan: Live temperature monitoring during destructive tests

## Goal
Integrate live disk temperature monitoring into the Sanitization (Absolute
Destructive) and Safe Destructive tests, so temperature is sampled continuously
during the write/read phases — without interrupting the test — and add an
automatic over-temperature warning with an offer to stop the test. Keep full
backward compatibility (older sessions without per-sample temperature must still
work via the existing session-level fallback).

## Background / root cause
Today temperature is only captured at discrete checkpoints. During the long
sanitization write/read loops the view models only re-report the baseline
temperature, so the live value never changes while the disk is actually heating up.
The Safe Destructive view model has a `TestCurrentTemperatureText` property that is
never populated, and no temperature UI at all.

Reading the SMART temperature attribute is a low-priority firmware query serviced
between I/O operations; it does not block or distort the sanitization. The correct
approach is a background sampler that polls temperature on a timer.

## Design decisions
1. New reusable service `DiskTemperatureMonitor` (implements `IDiskTemperatureMonitor`).
   - Wraps `ISmartaProvider.GetTemperatureOnlyAsync`.
   - Background loop at a configurable interval (default 5 s).
   - Exposes current/min/max temperature, a safe threshold, and events.
   - Threshold resolution: SMART attribute 194/190 threshold, else device-type
     default (HDD 60 °C, SSD/NVMe 70 °C).
   - Swallows read failures and keeps the last known value.
2. Wire into `AbsoluteDestructiveTestViewModel` (start/stop, live value, warning).
3. Wire into `SafeDestructiveTestViewModel` (start/stop, live value, warning, UI).
4. Backward compatible: no model/DB changes; monitor is additive; session-level
   fallback in `TestAnalysisDataService` still applies when no samples exist.
5. Localization keys added to cs.json and en.json.

## Files changed
- NEW `DiskChecker.Core/Interfaces/IDiskTemperatureMonitor.cs`
- NEW `DiskChecker.Application/Services/DiskTemperatureMonitor.cs`
- `DiskChecker.UI.Avalonia/ViewModels/AbsoluteDestructiveTestViewModel.cs`
- `DiskChecker.UI.Avalonia/ViewModels/SafeDestructiveTestViewModel.cs`
- `DiskChecker.UI.Avalonia/Views/SafeDestructiveTestView.axaml`
- `DiskChecker.UI.Avalonia/Locales/cs.json`, `en.json`
- `DiskChecker.UI.Avalonia/App.axaml.cs`
- NEW `tests/DiskChecker.Tests/DiskTemperatureMonitorTests.cs`

## Progress
- [x] Add `IDiskTemperatureMonitor` interface (Core/Interfaces)
- [x] Add `DiskTemperatureMonitor` service (Application/Services)
- [x] Register `IDiskTemperatureMonitor` in `App.axaml.cs`
- [x] Wire live monitoring into `AbsoluteDestructiveTestViewModel`
- [x] Wire live monitoring into `SafeDestructiveTestViewModel`
- [x] Add temperature readout to `SafeDestructiveTestView.axaml`
- [x] Add localization keys (cs.json, en.json)
- [x] Add tests (`DiskTemperatureMonitorTests.cs`)
- [x] Build & verify

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- Tests run via `dotnet exec .../DiskChecker.Tests.dll`:
  - `DiskTemperatureMonitorTests` → 8/8 passed.
  - Pre-existing `CertificateGeneratorTests` / `CertificateExportServiceTests`
    failures (25) are unrelated (permission denied on
    `/home/sa-admin/.config/DiskChecker/Certificates`).
- Test project compiles cleanly (0 errors).

## Notes
- `Stop()` was renamed to `StopMonitoring()` to satisfy CA1716 (reserved keyword).
- `CA1848` (LoggerMessage delegates) suppressed with a file-level pragma, matching
  the existing pattern in `CertificateExportService.cs`.
- The monitor is fully additive: when SMART is unsupported or the provider returns
  null, no samples are recorded and the existing session-level temperature fallback
  in `TestAnalysisDataService` continues to work unchanged.

---

# Implementation Plan: Non-SMART disks must not show grade A

## Goal
Disks that do not expose SMART data must never be graded. In the disk list the
grade badge must stay empty / show `N/A` instead of a misleading `A`.

## Root cause
`SmartctlJsonParser` parsed `smart_support.enabled` but ignored
`smart_support.available`. For devices without SMART (USB flash drives,
USB-SATA bridges without passthrough, RAID controllers) smartctl returns JSON
like:

```json
{ "model_name": "USB DISK 3.0",
  "smart_support": { "available": false, "enabled": false } }
```

The parser produced a non-null `SmartCheckResult` with `SmartStatusKnown == false`,
no attributes and no metrics. `ToSmartaData` then mapped "health unknown" to
`IsHealthy = true`, so `QualityCalculator` saw a penalty-free dataset and returned
**grade A**. In addition:

- `IsSmartSupportedAsync` returned `true` whenever a `model_name` was present,
  so the unavailable state was never propagated to `CoreDriveInfo.SupportsSmart`.
- `QualityCalculator.CalculateQuality(null)` manufactured a grade A rating
  ("neutral"), which any caller could display as a real grade.

## Fix
1. `SmartCheckResult.SmartAvailable` (`bool?`) + parse
   `smart_support.available`.
2. `SmartaData.IsSmartAvailable` (default `true`) filled from the parser.
3. `LinuxSmartaProvider` / `WindowsSmartaProvider`:
   - `GetSmartaDataAsync` returns `null` when smartctl explicitly reports
     SMART as unavailable (negative result cached via `NoSmartSentinel`, so
     `SupportsSmart` becomes `false` and the device is not queried again).
   - `ProbeSmartSupportLightweightAsync` returns `false` for
     `smart_support.available == false`, even when `model_name` is present.
4. `IQualityCalculator.CalculateQuality` now returns `QualityRating?` and
   returns `null` for missing SMART data or `IsSmartAvailable == false`.
   All call sites handle `null`; the UI renders `N/A`.
5. `DiskSelectionViewModel`:
   - disks with unavailable SMART show `N/A` (never `A`),
   - `SMART` unavailable system disk shows `N/A`,
   - unknown disks are no longer counted as "needs attention" in the dashboard.
6. Tests for parser, calculator, service and the disk list view model.

## Progress
- [x] Parser + models (`SmartAvailable`, `IsSmartAvailable`)
      - `SmartCheckResult.SmartAvailable` (`bool?`), parsed from `smart_support.available`
      - `SmartaData.IsSmartAvailable` (default `true`), filled in `ToSmartaData`
- [x] Providers (Linux/Windows): null for unavailable SMART, probe fix
      - `NoSmartSentinel.IsSmartAvailable = false`
      - `ExecuteSmartctlForSmartDataAsync` returns `null` when `available == false`
        (negative cache → `IsSmartSupportedAsync` returns `false` → `SupportsSmart = false`)
      - `ProbeSmartSupportLightweightAsync` returns `false` when `available == false`
        even if `model_name` is present
- [x] `QualityCalculator` nullable contract + call sites
      - `IQualityCalculator.CalculateQuality` → `QualityRating?`
      - `null` for missing data and for `IsSmartAvailable == false`
      - handled in `CertificationService`, `DiskCardTestService.ApplySmartQuality`,
        `DiskCheckerService`, `SmartCheckService`, `DiskSelectionViewModel`,
        `SmartCheckViewModel`
- [x] UI: disk list shows `N/A`, dashboard counters
      - `DiskStatusCardItem.GradeColor` (gray for `N/A`, F red, A green) used by the view
        instead of an always-green badge
      - system disk quick report shows `N/A` when SMART is unavailable
      - dashboard `HealthSummary` counts only gradeable disks; unknown disks are no longer
        reported as "needs attention"
- [x] Tests — `tests/DiskChecker.Tests/NonSmartGradeTests.cs` (17 tests)
      - parser: `smart_support.available` true/false/missing → `SmartAvailable` / `IsSmartAvailable`
      - calculator: null data → null, unavailable SMART → null, healthy → A, failing → F
      - `SmartCheckService`: unavailable SMART → `null`, nothing persisted
      - `DiskSelectionViewModel.LoadDriveAsync`: non-SMART → `N/A`, unavailable data → `N/A`,
        healthy disk → still `A`
      - `LinuxSmartaProvider` end-to-end against a fake `smartctl` binary:
        unavailable → `null` + `IsSmartSupportedAsync == false`, available → gradable data
- [x] Build & full test run
      - `dotnet build DiskChecker.slnx` → 0 errors (only NuGet security-advisory warnings)
      - full suite: **400 tests, 0 failed** (383 pre-existing + 17 new)

## Verification
- `dotnet build DiskChecker.slnx`
- `dotnet test` (or `dotnet exec` runner if the host binary cannot launch)
- Note: the test host executable fails in this environment (`Exec format error`), so the
  suite is run with
  `HOME=/tmp/dchome-test dotnet exec tests/DiskChecker.Tests/bin/Debug/net10.0/DiskChecker.Tests.dll`.
  A writable `HOME` is required — the certificate tests otherwise fail with
  `UnauthorizedAccessException` on `~/.config/DiskChecker/Certificates` (unrelated to this fix).

## Result
A device whose `smartctl` output contains `"smart_support": { "available": false }` is now
explicitly treated as "no SMART": the provider returns `null` (and caches that fact), the
quality calculator returns `null`, and the disk list shows `N/A` in a neutral gray badge
instead of a misleading `A`. Disks with real SMART data are graded exactly as before.

## Final verification (2026-10-26)
- `dotnet build DiskChecker.slnx` → `Počet chyb: 0` (20 warningů = jen NuGet advisory NU1902/NU1903).
- `HOME=/tmp/dchome-test dotnet exec tests/DiskChecker.Tests/bin/Debug/net10.0/DiskChecker.Tests.dll`
  → `DiskChecker.Tests  Total: 400, Errors: 0, Failed: 0, Skipped: 0`.
- `git status` – beze změn v `DiskChecker.db` (testy nezapisují do produkční DB).
- Ukázka z logu testu (`LinuxSmartaProvider` s fake `smartctl`):
  `smart_support.available=false ... device has no SMART, returning null`.
