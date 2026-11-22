# SMTP pull-request reconciliation: PR #15, #16, #17

## Repository state at inspection

On 2026-11-22, GitHub reported:

- **PR #16**: merged to `master` in commit
  `3d345e2d24d48810ad86708a2448a1191e461b91`.
  It contains the current SMTP security policy, real
  settings persistence, test email and certificate
  notification integration.
- **PR #18**: merged to `master` in commit
  `7dda64b7df3a586302846a5e662fb7d04346c295`,
  adding newer certificate output and installer work.
- **PR #15**: open, not mergeable, an older partial
  SMTP implementation against pre-#16 `master`.
- **PR #17**: open, not mergeable, 12 topic commits
  ahead but **67 commits behind** current master
  when inspected; 9 touched files. The branch
  overlaps SMTP changes superseded by #16.
  All **seven** inline Copilot review threads
  were marked resolved, but thread resolution
  alone is not proof that every equivalent
  production code path is correct.

Never force-merge or overwrite the contemporary
master files with the stale #15/#17 branches.
Closing those superseded branches (after recording
the disposition) is safer than reverting newer
SMTP/TLS, PDF, settings, localization or installer
changes while resolving conflicts.

## Review findings checked against current master

| Copilot concern | Current disposition |
| --- | --- |
| SMTP cancellation must not skip partition creation | Already solved in master: absolute destructive runs phase 7 *before* independently sending the stored certificate |
| SMTP cancellation must not fail the completed safe workflow | Already handled with a dedicated catch; strengthened by decoupling final delivery from navigation cancellation |
| SMTP cancellation must not report failed sanitization persistence | Master has separate email sending after saving the test, with delivery warnings treated independently |
| SMTP test button must save the recipient | Master uses `SaveSettingsCoreAsync`, which persists `ReportRecipientEmail` before the real test email |
| Restore-power status may erase an SMTP warning | Verified that master previously overwrote the final status with `PowerRestored`; fixed: successful restore no longer overwrites final disk/mail status and a restore error appends its own warning |
| Attached PDF needs actual series, not summaries | Full destructive sanitization series were populated; added missing ordered seek latency samples, and now fill safe destructive sanitization/profile/seek chart series and generic speed figures |
| Localized test label must not produce a seek-only PDF | Verified that the PDF renderer previously used the literal English `AbsoluteDestructive` to classify a comprehensive test; fixed by prioritizing structured observed sanitization + seek information |
| Navigation must not cancel a delivered certificate after disk completion | Final safe-destructive SMTP delivery is decoupled from the disk workflow cancellation token after restore/persistence |

`tests/DiskChecker.Tests/LocalizedCertificateCategoryTests.cs`
now exercises localized test names, actual comprehensive
classification, seek-only/read-only regression and
bounded chronological chart sampling.

The current SMTP implementation requires a trusted
certificate when TLS is enabled and explicit
`AllowInsecureSmtp` opt-in for a plaintext intranet
relay. Do not restore older branch behavior that
silently allows plaintext whenever TLS is disabled.

## Review constraints and verification

These fixes were committed **on current master**.
No physical destructive disk test was executed, and
the assistant environment cannot run .NET 10 unit
tests or inspect live SMTP delivery. The new changes
remain **pending focused/full CI and user runtime
verification**. A resolved or closed PR is not a
performance or real-world test result.

If any PR code still appears desirable, review it
against the current master before forward-porting
individual improvements. Do not merge the
pre-#16 branches wholesale.

See [install dependency notes](../Installer-Dependencies/en.md),
[SMTP test implementation status](../../IMPLEMENTATION_STATUS.md)
and the original [PR #17 review](https://github.com/afrowaveltd/DiskChecker/pull/17).
