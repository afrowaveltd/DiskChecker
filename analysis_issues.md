# Analýza problémů v Windows SMART implementaci

## Nalezené problémy:

### 1. WindowsSmartaProvider.GetSelfTestStatusAsync - chybí debug logging
Linux verze má Console.WriteLine debug výpisy, Windows verze je nemá.
To ztěžuje diagnostiku problémů.

### 2. WindowsSmartaProvider - chybí detekce typu zařízení (NVMe vs ATA)
Linux verze má metody `DetectDeviceType()` a `BuildSmartctlArgs()` které upravují
argumenty podle typu disku. Windows verze používá pouze `/dev/pd{N}` formát
bez ohledu na typ zařízení.

### 3. WindowsSmartaProvider.StartSelfTestAsync - chybí NVMe fallback
Linux verze detekuje "Invalid Field in Command" chyby pro NVMe a zkusí
znovu s `-d nvme` argumentem. Windows verze to nedělá.

### 4. SmartctlJsonParser.ParseAtaData - problém s detekcí InProgress
Parser hledá "in progress" string, ale pro ATA disky musí kontrolovat
i `status.value` podle ATA specifikace.

### 5. WindowsSmartaProvider.ExecuteSmartctlCommandAsync - nevrací error output
Linux verze loguje error output, Windows ho zahazuje, i když může obsahovat
důležité informace pro diagnostiku.

## Doporučené opravy:

1. Přidání debug logging do WindowsSmartaProvider
2. Přidání detekce NVMe disků pro Windows
3. Vylepšení parsování CurrentSelfTest status
4. Přidání fallback pro kontrolu SelfTests log