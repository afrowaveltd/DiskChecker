# Avalonia 1.0.0 pre-release audit — 2026-11-24

Baseline: master `72997a00096f`. At inspection, both master safety and surface/SMART workflows completed successfully, and PR #27 passed seven Windows/Linux checks including installer builds. No open issues or pull requests were listed before this audit.

## Corrected in this change

- JSON import maps original disk and session IDs to destination IDs; it keeps the session GUID and date, handles repeated imports and rejects invalid cross-disk certificate references before writes.
- Imported physical paths and external PDF/image paths are cleared. Local disk protection and paths are not overwritten when updating metadata.
- JSON export is explicitly a diagnostic summary transfer, not a complete database backup. It writes to a temporary sibling file and publishes the finished file only after successful serialization.
- Export/import/backup/restore navigation is guarded while operations own the scope. File picker paths use local filesystem paths.
- Czech and English labels reflect the actual non-destructive metadata-update import mode and summary-only JSON export.
- SQLite regression tests cover remapping, repeated Add/Replace imports, metadata and invalid relationships.

## Remaining release gates

- Review all current CI results, including actual installer build artifacts and checks.
- On expendable drives, test Windows and Linux read-only, seek, sanitization, safe destructive restore, absolute destructive and optional formatting. Verify that a protected system disk, a disconnected/replaced device, cancellation and a failure cannot cause unintended writes.
- Verify clean installation, upgrade, uninstall/reinstall, administrator/polkit startup, icon, SMART discovery and optional filesystem utilities on both platforms. Windows currently bundles a verified smartmontools prerelease; confirm it is the intended release dependency.
- Check PDF single/bundle, regenerated English report from Czech UI, historical charts, report folder access, PDF viewer and SMTP attachment delivery on real systems.
- Test SQLite database backup/restore on a disposable database, including restart after restore. Verify PostgreSQL/SQL Server modes if advertised as supported.
- JSON transfer currently omits raw sample/telemetry/SMART snapshot tables; use the database backup for complete history. It is not a transaction across the entire import, and cancellation/failure can leave already committed records. Further work: atomic import and full, bounded-memory telemetry transfer.
- Audit remaining hard-coded strings, including post-test formatting dialogs and old diagnostic text; Arabic/RTL and complete retranslation of historical notes remain out of scope for 1.0.0 unless explicitly chosen as release gates.
- Documentation and artifact hashes should match the final tag; do not publish 1.0.0 until manual release gates are accepted.

This is a targeted source review, not a certification of all application paths or an assertion that physical tests have been executed by the reviewer. TUI visual redesign is separate.
