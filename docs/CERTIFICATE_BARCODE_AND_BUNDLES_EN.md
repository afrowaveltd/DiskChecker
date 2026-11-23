# Disk barcodes and combined certificates (1.0)

## Scan a disk certificate
Every newly generated PDF certificate shows a **Code 128** barcode in the
top-right printable area. It identifies the **disk**, not the individual
certificate or test. The certificate number and issue time are also retained
as printed text; long identifiers are clipped to the printable header width.

- Code `DS` + 16 digits: deterministic short identity derived from the
  normalized actual hardware serial via SHA-256. The barcode is a lookup key,
  **not** an encrypted serial and **not** an internet URL.
- Code `DL` + 16 digits: local card ID fallback when the physical serial is
  missing, marked `NOSN-...`, `N/A`, `Unknown`, etc. This form works
  **only with the originating local database** and should not be assumed to
  identify the disk in a separate installation.
- A database may contain duplicate cards for the same serial. If a scan
  matches more than one card, the app reports ambiguity instead of opening
  an arbitrary disk.

Most standard USB Code 128 barcode scanners operate as a keyboard. Place
focus in the global scan box in the main window header, scan, then press
Enter (or configure the scanner to append Enter). A manual serial number is
also accepted. A matching card opens its full saved history. Existing Disk
Cards and Certificate Browser search fields also accept barcode text.

**Important:** disk barcode lookup is disabled while a running destructive
operation locks navigation. A scan does not authorize disk writes.

## Create a combined report
1. Open **Disk Cards**, then the desired saved disk card.
2. In **Test History**, select **two or three rows** from different test types
   (e.g. SEEK + Sanitization or SEEK + Sanitization + SMART). Hold **Ctrl**
   while clicking individual rows, or **Shift** to select a continuous range.
   Then click **Combined PDF (2–3)** at the top of the card.
3. Alternatively open the **Certificates** tab and tick two or three
   different existing certificates, then use **Combined PDF (2–3)**.
   Explicitly checked certificates take precedence over highlighted history
   rows, so uncheck them to export the history selection instead.
4. The new PDF is saved in the configured Certificates directory. You may
   open it after export.

Older versions of DiskChecker may have test sessions but no saved certificate
rows. History-based export will reconstruct any missing certificate **only in
memory**, using the stored test session. No new certificate records or test
results are saved to the database. For sessions with a stored certificate,
that original certificate is preferred (including its original grade and
technician assessment).

The report has a front summary page and **one full A4 page for each
certificate** (3 or 4 pages total). Each original test grade, score, date,
graph, performance metric and technician comment remains independent.
The operation does not change the source certificates, stored sessions,
scores or SMART data, and does not create a fictitious overall grade.
The existing automatic two-test pairing export remains available.

If a selected certificate was removed or its underlying test belongs to
another disk, export is rejected. Chart samples are loaded with bounded
downsampling so old large sessions need not be loaded entirely into memory.

## Verification before public release
The automated test suite checks deterministic lookup identities, missing
serial fallbacks, Code 128 symbol/checksum rules, mixed-card rejection,
duplicate-session rejection, category uniqueness, and a real generated
multi-page PDF. Physical printed labels and the exact USB scanner model
still require one manual scan test before announcing a public final release.
