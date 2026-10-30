# Audit databází a historie disků — 30. října 2026

## Zjištěné chyby

1. Avalonia používala výchozí relativní SQLite cestu `Data Source=DiskChecker.db`, jejíž fyzický soubor se lišil podle pracovního adresáře procesu.
2. `SettingsService` nabízela výběr SQLite/PostgreSQL/SQL Serveru, ale nastavení databáze **neukládala ani nenačítala** ze `settings.json`.
3. `BackupService` vždy zálohovala databázi v adresáři spustitelného programu místo aktivní databáze; kopírování SQLite souboru za běhu navíc nemuselo zachovat transakce ve WAL.
4. Registrační životnost `SmartCheckService` a `DataExportImportService` byla `Singleton`, přestože závisejí na databázových objektech se `Scoped` životností.
5. `DiskCardRepository.CreateAsync` slučoval fyzicky různé disky při shodném OS device path. `DiskCardDetailViewModel` používal podobně nebezpečný path-only fallback.

## Implementovaná ochrana

- `SettingsService` nyní ukládá `DatabaseProvider` a `DatabaseConnectionString` a na prvním startu připne SQLite databázi na absolutní cestu. Na dalších startech používá připnuté umístění. Výběr při první inicializaci bez uložené konfigurace: existující DB v CWD → existující DB vedle programu → DB v uživatelském adresáři konfigurace. Pro explicitně poskytnutý testovací settings adresář se používá jen tento adresář.
- Žádná historická SQLite DB se automaticky nepřesouvá, nemaže ani neslučuje. Další nalezené soubory se ukazují ve stavu Nastavení.
- Avalonia zachovává jednu aktivní konfiguraci DB od startu do restartu. Změna backendu v Nastavení platí až po restartu.
- SQLite řetězce s relativní cestou se normalizují na absolutní.
- Záloha SQLite využívá `Microsoft.Data.Sqlite.SqliteConnection.BackupDatabase` nad *aktivní* DB (včetně konzistentního WAL snapshotu), nikoliv slepou kopii souboru. Obnova vytváří předběžnou `.pre_restore_<timestamp>` zálohu aktivní DB a zachovává její konfigurovanou cestu. Pro jiné backendy zatím použijte jejich nativní zálohovací nástroje.
- Nové ZIP zálohy se ukládají do podsložky `Backups` vedle aktuálního `settings.json` (uživatel potvrdil, že dosavadní zálohování nepoužíval).
- Při zakládání Karty disku už není shoda `PhysicalDriveN` ani `/dev/sdX` důvodem pro sloučení různých sériových čísel.
- Upraveny životnosti DI a přidány regresní testy.

## Důležité hranice

- Toto je audit cesty k databázi, hlavních závislostí a identity historických disků, nikoli certifikace celé aplikace. Nové změny musí projít `dotnet build DiskChecker.slnx -c Release` a testy; při provádění úprav nebyl lokálně k dispozici .NET SDK.
- Samostatná `DiskChecker.TUI` má záměrně **jiný** SQLite soubor `%LOCALAPPDATA%/DiskChecker/dcheck.db` a jiný datový model. Nejde o druhý kontext Avalonia DB; sjednocení TUI schématu je samostatný úkol.
- `DiskChecker.Infrastructure/ServiceCollectionExtensions.AddPersistenceServices(dataDirectory)` je alternativní metoda registrace; v současném startu Avalonia není používána. Nesmí se připojit paralelně k registraci z `App.axaml.cs`.
- Při existenci více historických souborů `DiskChecker.db` nelze bez inspekce obsahu určit, který je kompletnější. Před ručním přepnutím je nutno oba soubory nezávisle zazálohovat.
- Záloha samotné SQLite DB není kompletní archiv externí telemetrie `%LOCALAPPDATA%/DiskChecker/Telemetry`, grafů a PDF. Přidání souvisejících souborů do sjednocené zálohy je samostatná funkce.

## Doporučená kontrola po stažení změn

```powershell
git pull origin master
dotnet build DiskChecker.slnx -c Release
dotnet test DiskChecker.slnx -c Release --filter "FullyQualifiedName~DatabaseStorageConsistencyTests|FullyQualifiedName~DiskCardIdentityPersistenceTests"
```

Poté otevřete Nastavení a zkontrolujte aktivní SQLite cestu i případné upozornění na další existující soubory. Proveďte neškodný SMART test a ověřte jeho zobrazení ve správné Kartě disku, poté zavřete aplikaci, spusťte znovu a ověřte stejnou historii.
