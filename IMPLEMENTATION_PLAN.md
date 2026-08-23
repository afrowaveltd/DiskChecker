# Implementation Plan: Fix crash after Seek Test → navigate to Disk Cards

## Goal
Fix an intermittent crash (Windows + Linux) that occurs after a Seek Test completes and the
user navigates to "Karty disků" (Disk Cards). Also review navigation robustness in general.

## Root cause
`SeekTestViewModel` is registered as **Transient** and implements `IDisposable`. When the user
navigates away, `NavigationService.NavigateTo<T>()` disposes the current DI scope, which disposes
the `SeekTestViewModel` (and its scoped `DiskCardRepository`/`DbContext`).

However, the Seek Test uses `Dispatcher.UIThread.Post(...)` in two places that can still fire
**after** the view model has been disposed:

1. The real-time progress callback (invoked from the background seek loop) posts to the UI thread
   and mutates `LatencyChartValues` / `LatestSample` / chart axes.
2. `BuildFinalChart(...)` posts a two-step assignment of `FinalLatencySeries` / `FinalLatencyXAxes`
   / `FinalLatencyYAxes` to force a LiveCharts2 SkiaSharp redraw.

When the user navigates to Disk Cards immediately after the test finishes, the pending
`Dispatcher.UIThread.Post` callbacks run against a disposed view model whose `CartesianChart`
controls are being detached from the visual tree. LiveCharts2 SkiaSharp then throws while trying
to re-render a destroyed chart surface — an intermittent (race-condition) crash, hence
"ne vždy, ale velmi často".

The `_isFinalChartBuilt` flag only guards against progress callbacks corrupting the *final* chart;
it does **not** guard against the view model being disposed.

## Fix
- Add a `_disposed` guard to every `Dispatcher.UIThread.Post` callback in `SeekTestViewModel`
  (progress callback + `BuildFinalChart`), so they become no-ops after `Dispose()`.
- `Dispose()` already sets `_disposed = true` first, so pending posts are safely ignored.

## Navigation robustness review (same latent pattern)
The same "transient IDisposable ViewModel + unguarded `Dispatcher.UIThread.Post`" pattern exists
in other test/operation ViewModels. Applied the same `_disposed` guard to prevent the same
class of crash when navigating away mid-operation:

- `SurfaceTestViewModel` — added `_disposed` field + guard in `AddSpeedPoint` and the sanitization
  `ApplyProgress` post; made `Dispose()` idempotent.
- `AbsoluteDestructiveTestViewModel` — guarded 7 `Dispatcher.UIThread.Post` callbacks.
- `SafeDestructiveTestViewModel` — guarded 1 `Dispatcher.UIThread.Post` callback.
- `RestoreViewModel` — guarded 1 `Dispatcher.UIThread.Post` callback.

## Progress
- [x] Analyze root cause
- [x] Guard SeekTestViewModel progress callback post with `_disposed`
- [x] Guard SeekTestViewModel BuildFinalChart post with `_disposed`
- [x] Harden SurfaceTestViewModel / AbsoluteDestructiveTestViewModel / SafeDestructiveTestViewModel / RestoreViewModel
- [x] Build & verify (0 errors)

## Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` could not run in this environment (`Exec format error` on the test host exe —
  pre-existing environment limitation, unrelated to this change).

---

## FullStroke Seek Test Grading (user request #2)

### Requirement
Grade FullStroke seek tests by absolute average latency, less strict than the
consistency/reliability score used for other test types:
- avg < 15ms → A
- avg < 20ms → B
- avg < 25ms → C
- avg < 30ms → D
- avg >= 30ms → E
- failure (aborted / incomplete / errors / no samples) → F

### Change
- `SeekTestViewModel.CalculateSeekGrade` now branches on `result.TestType`:
  `FullStroke` → new `CalculateFullStrokeGrade`, other types → existing score-based switch.
- New `CalculateFullStrokeGrade`:
  - returns "F" if `!IsCompleted || WasAborted || ErrorCount > 0` or no successful samples;
  - otherwise grades by `AverageLatencyMs` (falling back to sample average) against the thresholds above.

### Tests
- Added `SeekTestGradeTests` in `tests/DiskChecker.Tests/SeekTestTests.cs` invoking the private
  `CalculateSeekGrade` via reflection, covering all thresholds (A–E) and failure cases (aborted,
  not completed, errors, no samples).

### Verification
- `dotnet build DiskChecker.slnx` → 0 errors.
- `dotnet test` still cannot run in this environment (`Exec format error` on test host exe —
  pre-existing limitation).
