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
- [ ] Commit & push
