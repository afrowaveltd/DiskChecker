# Implementation Plan: DataGrid column resize & sorting

## Goal
In the Avalonia UI, for every view that contains a list/table of items (DataGrid),
enable:
1. **Column resizing** (`CanUserResizeColumns="True"`)
2. **Column sorting** (`CanUserSortColumns="True"`) where it makes sense

Ensure nothing breaks. Work step by step, one view at a time.

## Inventory of DataGrids (tables)

| View | DataGrid(s) | Item type |
|------|-------------|-----------|
| ReportView.axaml | 1 (report list) | TestReportItem |
| HistoryView.axaml | 1 (test history) | HistoricalTest |
| DiskCardsView.axaml | 1 (disk cards) | DiskCard |
| DiskCardDetailView.axaml | 3 (test sessions, SMART history, certificates) | TestSession / SmartHistoryItem / DiskCertificate |
| CertificateBrowserView.axaml | 1 (certificate list) | CertificateListItem |
| SmartCheckView.axaml | 1 (self-test log) | SmartaSelfTestEntry |

Note: `DiskComparisonView.axaml` uses a plain `Grid` layout (not a DataGrid), so it is
out of scope.

## Approach
- Add `CanUserResizeColumns="True"` and `CanUserSortColumns="True"` to each DataGrid.
- Add `SortMemberPath` to columns whose `Binding` targets a *computed/formatted string*
  property, so sorting uses the underlying typed value (e.g. `ScoreText` -> `Score`,
  `GeneratedAtText` -> `GeneratedAt`).
- Add `CanUserSort="False"` to action/template columns where sorting is meaningless
  (e.g. "Actions", "Volumes", "Signals" badge columns).

## Progress
- [x] Analyze project & inventory DataGrids
- [x] ReportView.axaml (already had both flags — no change needed)
- [x] HistoryView.axaml
- [x] DiskCardsView.axaml
- [x] DiskCardDetailView.axaml (3 grids)
- [x] CertificateBrowserView.axaml
- [x] SmartCheckView.axaml
- [x] Build & verify (0 errors)
- [x] Commit & push

---

# Implementation Plan: Open generated PDF in default app under sudo (Linux)

## Goal
When the app runs under `sudo` on Linux, opening a generated PDF (or label) in the
default system application fails because `xdg-open` runs as root and cannot reach the
desktop user's session (missing `DISPLAY`/`XAUTHORITY`/DBus context). The file should
instead be opened under the account of the currently logged-in user.

## Root cause
`DocumentLauncher.OpenFile` uses `Process.Start(UseShellExecute = true)`, which on Linux
invokes `xdg-open` as the current (root) user. Under `sudo`, `SUDO_USER`/`SUDO_UID` are
set, but the desktop session environment (`XAUTHORITY`, `DISPLAY`, DBus) is not inherited,
so the viewer never appears.

## Approach
- In `DocumentLauncher.OpenFile`, before the generic shell open, detect `SUDO_USER`
  (non-root) on non-Windows platforms.
- Resolve the original user's X authority file (`/run/user/<uid>/gdm/Xauthority`,
  `/run/user/<uid>/xauth_*`, `~/.Xauthority`).
- Launch `xdg-open <file>` as the original user via `runuser -u <user> -- env ... xdg-open`
  (fallback to `su <user> -c ...`), passing `DISPLAY` and `XAUTHORITY`.
- Keep Windows behavior unchanged.

## Progress
- [ ] Implement `TryOpenAsSudoUser` in DocumentLauncher
- [ ] Build & verify
- [ ] Commit & push
