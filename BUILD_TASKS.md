# Seznam úloh pro opravu chyb kompilace

##analyzovat chyby kompilace
### Přehled chyb podle projektu:

**DiskChecker.Application** - 28 chyb
- DiskCheckerService.cs: 5 chyb (GetSmartaDataAsync, IsDriveValidAsync, ListDrivesAsync)
- CertificationService.cs: 2 chyby (GetSmartaDataAsync, GenerateCertificate)
- SmartCheckService.cs: 12 chyb (různé SMART/metody a typy)
- SurfaceTestService.cs: 1 chyba (Create metoda)

### Metody, které mají nesprávné přetížení:
1. `GetSmartaDataAsync(drive, token)` - chybně volá 2 argumenty
2. `IsDriveValidAsync(drive, token)` - chybně volá 2 argumenty
3. `ListDrivesAsync(token)` - chybně volá 1 argument
4. `Create(config)` - chybně volá 1 argument
5. `GetSmartAttributesAsync(drive, token)` - chybně volá 2 argumenty
6. `GetSelfTestStatusAsync(drive, token)` - chybně volá 2 argumenty
7. `GetSelfTestLogAsync(drive, token)` - chybně volá 2 argumenty
8. `GetSupportedMaintenanceActionsAsync(drive, token)` - chybně volá 2 argumenty
9. `ExecuteMaintenanceActionAsync(drive, action, token)` - chybně volá 3 argumenty
10. `StartSelfTestAsync(drive, type, token)` - chybně volá 3 argumenty
11. `GetTemperatureOnlyAsync(drive, token)` - chybně volá 2 argumenty
12. `GetDependencyInstructionsAsync(token)` - chybně volá 1 argument
13. `TryInstallDependenciesAsync(token)` - chybně volá 1 argument

### Chyby typu:
1. `double` nemá `GenerateCertificate`, `Grade`, `Score`
2. Převod `double` na `QualityRating`
3. Převod `string` na `SmartaSelfTestStatus?`
4. `SmartaSelfTestStatus.Contains()` - není dostupné

## Strategie řešení:
1. Zkontrolovat rozhraní ISmartService a její implementace
2. zkontrolovat rozhraní ISmartaProvider
3. zkontrolovat metody v rozhraní ISurfaceTestService
4. Zkontrolovat rozšíření (extension methods)
5. Zkontrolovat typy modelů
