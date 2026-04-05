# Návrh vylepšení povrchového testu pro rané ukončení při opakovaných chybách

## Problém
Při povrchovém testu disku, pokud disk přestaně číst nebo zapisovat data (např. kvůli fyzickému poškození), současná implementace pokračuje v testování i přes opakované chyby čtení/zápisu. To vede k:
1. Zbytečnému prodlužování testu na již známě vadném disku
2. Zbytečnému opotřebovávání potenciálně poškozeného disku dalším testováním
3. Neefektivnímu využití času - test mohl být ukončen mnohem dříve

## Řešení
Přidat logiku pro sledování po sobě jdoucích neúspěšných pokusů o čtení/zápis a předčasné ukončení testu, pokud tento počet překročí definovaný práh.

## Implementační detaily

### Kde implementovat
Logikuearly ukončení je třeba implementovat v místě, kde dochází k skutečnému čtení nebo zápisu diskových sektorů - pravděpodobně v metodách zodpovědných za:
- Čtení sektorů z disku
- Zápis sektorů na disk
- Ověřování integrity dat po zápisu

### Algoritmus
1. Definovat práh pro po sobě jdoucí chyby (např. 10 po sobě jdoucích neúspěšných operací)
2. Během testování sledovat počet po sobě jdoucích neúspěšných pokusů:
   - Při úspěšné operaci čtení/zápisu: resetovat čítač na 0
   - Při neúspěšné operaci čtení/zápisu: inkrementovat čítač
3. Pokud čítač po sobě jdoucích chyb překročí práh:
   - Ukončit test předčasně
   - Označit výsledek testu jako chybný kvůli příliš mnoha chybám
   - Uložit do poznámek konkrétní informaci o příčině ukončení

### Příklad implementace (pseudokód)
```csharp
// Definice prahu - nastavitelná konstanta
private const int MaxConsecutiveErrors = 10;

// Vnitřní stav během testu
private int consecutiveErrorCount = 0;

// Metoda pro čtení sektoru s kontrolou na opakované chyby
private async Task<bool> ReadSectorWithErrorHandling(
    IntPtr handle, 
    long sectorOffset, 
    byte[] buffer, 
    uint sectorSize,
    CancellationToken cancellationToken)
{
    // Pokus o přečtení sektoru
    bool success = await ReadSectorInternal(handle, sectorOffset, buffer, sectorSize, cancellationToken);
    
    if (success)
    {
        // Úspěšná operace - resetovat čítač chyb
        consecutiveErrorCount = 0;
        return true;
    }
    else
    {
        // Neúspěšná operace - inkrementovat čítač
        consecutiveErrorCount++;
        
        // Kontrola, zda jsme překročili prah
        if (consecutiveErrorCount >= MaxConsecutiveErrors)
        {
            // Předčasné ukončení testu
            throw new SurfaceTestException(
                $"Test terminated early due to {consecutiveErrorCount} consecutive read errors. " +
                $"This indicates a severely damaged or failing disk.");
        }
        
        return false;
    }
}

// Podobná logika by byla třeba přidat pro zápis sektorů
private async Task<bool> WriteSectorWithErrorHandling(
    IntPtr handle, 
    long sectorOffset, 
    byte[] buffer, 
    uint sectorSize,
    CancellationToken cancellationToken)
{
    // Pokus o zápis sektoru
    bool success = await WriteSectorInternal(handle, sectorOffset, buffer, sectorSize, cancellationToken);
    
    if (success)
    {
        // Úspěšná operace - resetovat čítač chyb
        consecutiveErrorCount = 0;
        return true;
    }
    else
    {
        // Neúspěšná operace - inkrementovat čítač
        consecutiveErrorCount++;
        
        // Kontrola, zda jsme překročili prah
        if (consecutiveErrorCount >= MaxConsecutiveErrors)
        {
            // Předčasné ukončení testu
            throw new SurfaceTestException(
                $"Test terminated early due to {consecutiveErrorCount} consecutive write errors. " +
                $"This indicates a severely damaged or failing disk.");
        }
        
        return false;
    }
}

// Vlastní výjimka pro předčasné ukončení testu
public class SurfaceTestException : Exception
{
    public SurfaceTestException(string message) : base(message) { }
}
```

### Integrace do výsledků testu
Když dojde k předčasnému ukončení kvůli výjimce `SurfaceTestException`:
1. Chytit tuto výjimku v hlavním cyklu testování
2. Nastavit `CompletedAtUtc` na aktuální čas
3. Nastavit `Notes` na zprávu z výjimky (která již obsahuje informaci o příčině ukončení)
4. Vrátit výsledek testu (i když je neúplný, obsahuje užitečné informace o stavu disku)

### Výhody tohoto přístupu
1. **Okamžitá reakce na selhání disku** - test není nutné dokončovat, pokud je již zřejmé, že disk je poškozen
2. **Ochrana disku** - zabraňuje zbytečnému opotřebovávání poškozeného disku dalším testováním
3. **Informativní výsledky** - uživatel se přesně dozví, proč byl test ukončen
4. **Zpětná kompatibilita** - nevyžaduje změny v datovém modelu ani databázi
5. **Konfigurovatelnost** - práh pro ukončení lze upravit podle potřeby (citlivější nebo tolerantnější nastavení)

### Doporučená hodnota prahu
- **MaxConsecutiveErrors = 10** - dobrý kompromis mezi:
  - Dostatečně vysoký, aby nebyl překročen kvůli náhodným chybám
  - Dostatečně nízký, aby zachytil skutečné selhání disku
  - Alternativně lze udělat tento práh konfigurabilním přes uživatelské rozhraní nebo konfigurační soubor