# DiskChecker implementační plán

## Kontekst
- UI: konzole (Spectre.Console) a Blazor Server mají být rovnocenné.
- .NET 10, Windows + Linux.
- Používat pouze knihovny a nástroje s volnou firemní licencí (MIT/Apache/BSD/MS-PL apod.), bez restrikcí pro interní využití.

## Aktuální stav (shrnutí)
- SMART: čtečky pro Windows/Linux existují, ale parsování je minimální.
- UI: konzole i web jsou zatím jen základní/placeholder.
- Persistence: SQLite přes EF Core, základní tabulky pro testy a SMART.
- Certifikáty: generátor textového certifikátu existuje.

## Cíle
1. SMART kontrola s lidsky čitelným výstupem a „minimal“ režimem.
2. Kompletní povrchový test (zápis/čtení) se záznamem rychlostí a výsledků.
3. Certifikát (HTML + tisk) z DB, volitelně email.
4. Replikace dat do centrální DB (nastavitelně: batch/on-demand/stream).
5. Parita funkcí konzole + web.
6. Testy: xUnit + NSubstitute pro všechny nové služby.

## Fáze a kroky
### Fáze 1 – Architektura a licenční audit
- [ ] Zmapovat všechny použité knihovny a externí nástroje (Windows/Linux).
- [ ] Ověřit licence (povolené pro interní firemní použití).
- [ ] Definovat pravidla pro závislosti a seznam schválených licencí.

### Fáze 2 – Doménové workflow + modely
- [ ] Ujednotit workflow: Discovery → SMART snapshot → Quality rating → Persist.
- [ ] Rozšířit entity:
  - `TestRun`, `SurfaceTestResult`, `SpeedSample`, `CertificateRecord`, `ReplicationQueue`.
- [ ] Přidat konfiguraci EF Core pro více providerů (SQLite lokálně + volitelně SQL Server/PostgreSQL).

### Fáze 3 – SMART a validace disku
- [ ] Vylepšit parsing SMART pro Windows/Linux (strukturovaný parsing, robustní mapování atributů).
- [ ] Přidat „minimal mode“ výstup (SMART + známka bez zbytku workflow).
- [ ] Uložit SMART snapshoty do DB.

### Fáze 4 – Povrchový test
- [ ] Navrhnout cross‑platform testovací službu:
  - Sekvenční zápis/čtení se vzorky rychlosti.
  - Konfigurovatelná velikost bloku a hloubka testu.
- [ ] Uložit výsledky a rychlosti do DB.
- [ ] Připravit bezpečnostní ochrany (explicitní potvrzení, jasný výběr zařízení).

### Fáze 5 – Certifikáty a reporty
- [ ] Generovat HTML certifikát z DB.
- [ ] Přidat export do souboru a tiskový layout.
- [ ] Volitelný email přes SMTP konfiguraci.

### Fáze 6 – UI parita (Console + Web)
- [ ] Sdílené view modely a služby v `Application`.
- [ ] Konzole: výběr disku, SMART výpis, test s průběhem, historie, porovnání.
- [ ] Web: stejné obrazovky a akce + detailní graf rychlosti.

### Fáze 7 – Replikace do centrální DB
- [ ] Implementovat `IReplicationService` s režimy: batch/on-demand/stream.
- [ ] Fronta změn a retry politika.
- [ ] Konfigurační zásady (appsettings + UI nastavení).

### Fáze 8 – Testy
- [ ] xUnit + NSubstitute pro služby (SMART parsing, quality, test workflow).
- [ ] Testy persistence (EF Core InMemory/Sqlite in-memory).
- [ ] Testy UI logiky (view‑modely, mappingy, validace).

## Otevřené otázky
- Jaký nástroj/strategii zvolit pro nízkoúrovňový povrchový test na Linux/Windows s akceptovatelnou licencí?
- Požadované centrální DB: SQL Server vs. PostgreSQL?
- Má email používat SMTP nebo integrování s interní službou?

## Milníky
- M1: SMART + Minimal mode + DB ukládání.
- M2: Povrchový test + grafy rychlosti.
- M3: Certifikát + email/export.
- M4: UI parita + replikace.
