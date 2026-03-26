# Akční plán pro řešení problémů v DiskChecker

## Priorita 1: Vysoká (kritické problémy ovlivňující uživatelský zážitek)

### Úkol 1.1: Optimalizace SMART dotazů (problém č. 1)
- [ ] Prozkoumat aktuální implementaci WindowsSmartaProvider.GetSmartaDataAsync
- [ ] Implementovat efektivní cachování s TTL (už částečně přítomné, potřebuje vylepšení)
- [ ] Optimalizovat volání smartctl pro minimalizaci počtu procesů
- [ ] Přidat diagnostické sledování výkonu
- [ ] Testovat s různými typy disků (SATA, NVMe, USB)

### Úkol 1.2: Oprava chybějících SMART údajů (problém č. 2)
- [ ] Aktualizovat SmartctlJsonParser pro extrakci všech kritických atributů
- [ ] Rozšířit TestRecord model o chybějící SMART pole
- [ ] Opravit mapování v CertificateGenerator.cs
- [ ] Vylepšit generování grafu v PDF reportu

## Priorita 2: Střední (problémy ovlivňující správnost dat)

### Úkol 2.1: Oprava detekce disků a výrobních čísel (problém č. 3)
- [ ] Prozkoumat DriveIdentityResolver.BuildIdentityKey
- [ ] Opravit generování identity klíče pro různé typy cest
- [ ] Vylepšit detekci duplicitních disků v DiskCardTestService
- [ ] Přidat normalizaci a validaci sériových čísel
- [ ] Zlepšit zpracování chybějících dat

### Úkol 2.2: Konsistence certifikátů (problém č. 4)
- [ ] Analyzovat současný postup generování certifikátů
- [ ] Identifikovat místa, kde dochází k nekonzistenci
- [ ] Implementovat transakční přístup k generování
- [ ] Zavést systém stavů certifikátů
- [ ] Přidat detekci a řešení duplicitních certifikátů

## Priorita 3: Nízká (lepší uživatelský zážitek)

### Úkol 3.1: Monitoring a diagnostika
- [ ] Přidat podrobnější logování napříč komponentami
- [ ] Implementovat metriky výkonu pro kritické operace
- [ ] Přidat možnost náhledu do interního stavu aplikace

### Úkol 3.2: Dokumentace a údržba
- [ ] Aktualizovat dokumentaci k provedeným změnám
- [ ] Přidat komentáře ke složitým částem kódu
- [ ] Vytvořit přehled provedených optimalizací
