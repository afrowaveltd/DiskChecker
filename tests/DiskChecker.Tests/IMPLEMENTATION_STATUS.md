# Test implementation status

## 2026-11-23 — initial durable long-test checkpoint regressions

Added `LongTestRecoveryJournalTests` for
startup inventory, in-progress progress checkpoint,
full final sanitization and graph sample round trip,
database-vs-artifact states, explicit failed/cancelled
outcomes, corrupt-file survival, mismatch prevention
and no automatic raw disk resume.

Tests operate exclusively on temporary directories,
fake device descriptors and fabricated outcomes.
No real physical device is opened, wiped or resumed.
CI validation pending.

## 2026-11-23 — reconciliation persistence tests

Added isolated, fake-disk EF InMemory
integration tests for complete terminal
failure import, exact RunId idempotence,
refusal of unfinished/corrupt records,
and persistent proof-based cleanup.
These tests do not open or overwrite raw
devices and do not access the maintainer's
real SQLite or telemetry files.
CI pending.
