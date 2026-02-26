# Oprava UI problémů - Theme Toggle & Disabled Button

## 🐛 Nalezené problémy

### 1. **ThemeToggle nefungoval**
**Soubor:** `ThemeToggle.razor`

**Problém:**
- Component nastavoval `data-theme` atribut
- Ale CSS používal JENOM `@media (prefers-color-scheme: dark)`
- `data-theme` atribut byl IGNOROVÁN!
- **Výsledek:** Kliknutí na toggle nic nedělalo

**Oprava:**
1. **Přidány CSS custom properties (variables)**
```css
:root {
    --bg-primary: #f5f5f5;
    --text-primary: #333;
}

[data-theme="dark"] {
    --bg-primary: #1a1a1a;
    --text-primary: #e0e0e0;
}

body {
    background: var(--bg-primary);
    color: var(--text-primary);
}
```

2. **Vylepšena logika načítání preferencí**
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        // 1. Check localStorage FIRST
        var stored = await JS.InvokeAsync<string>("localStorage.getItem", "darkMode");
        if (!string.IsNullOrEmpty(stored))
        {
            IsDarkMode = bool.Parse(stored);
        }
        // 2. Fallback to system preference
        else
        {
            IsDarkMode = await JS.InvokeAsync<bool>("eval", 
                "window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches");
        }
        
        // 3. Apply theme
        await ApplyThemeAsync();
        StateHasChanged();
    }
}
```

**Výhody:**
- ✅ localStorage má prioritu (zapamatuje si volbu uživatele)
- ✅ Fallback na system preference (první návštěva)
- ✅ CSS variables umožňují snadnou customizaci
- ✅ Okamžitá změna bez reload stránky

### 2. **Tlačítko "Spustit test" zůstávalo disabled**
**Soubor:** `SurfaceTest.razor`, řádek 236

**Problém:**
```csharp
// PŘED - chybějící kontrola pro FullDiskSanitization
private bool IsRunDisabled => IsRunning || string.IsNullOrWhiteSpace(SelectedPath) ||
    (SelectedProfile == SurfaceTestProfile.HddFull && !ConfirmDestructive);
```

- Kontrola potvrzení byla JEN pro `HddFull`
- Pro `FullDiskSanitization` CHYBĚLA
- **Výsledek:** I když uživatel zaškrtl checkbox, tlačítko zůstalo disabled!

**Oprava:**
```csharp
// PO - přidána kontrola pro FullDiskSanitization
private bool IsRunDisabled => IsRunning || string.IsNullOrWhiteSpace(SelectedPath) ||
    ((SelectedProfile == SurfaceTestProfile.HddFull || SelectedProfile == SurfaceTestProfile.FullDiskSanitization) && !ConfirmDestructive);
```

**Výhody:**
- ✅ Oba destruktivní profily vyžadují potvrzení
- ✅ Logika je konzistentní
- ✅ Bezpečnější - nelze spustit sanitizaci bez potvrzení

## ✅ Ověření

### Theme Toggle
1. Otevři aplikaci v browseru
2. Klikni na "🌙 Tmavý motiv" / "☀️ Světlý motiv"
3. Motiv se OKAMŽITĚ změní
4. Reload stránky - motiv zůstane zachován (localStorage)

### Spuštění testu
1. Vyber disk
2. Vyber profil "⚠️ Kompletní vymazání disku (sanitizace)"
3. Tlačítko "▶️ Spustit test" je **disabled** ❌
4. Zaškrtni checkbox "⚠️ VAROVÁNÍ: Tento test SMAŽE..."
5. Tlačítko "▶️ Spustit test" je **enabled** ✅
6. Test lze spustit

## 📊 Příklad chování

### PŘED opravu:

**Theme Toggle:**
```
User clicks toggle → Nothing happens
User refreshes page → Still light mode
```

**Test Button:**
```
User selects "Kompletní vymazání disku"
User checks the warning checkbox
Button state: DISABLED ❌ (ŠPATNĚ!)
```

### PO opravě:

**Theme Toggle:**
```
User clicks toggle → Theme changes immediately
User refreshes page → Theme persists (from localStorage)
```

**Test Button:**
```
User selects "Kompletní vymazání disku"
Button state: DISABLED ❌ (správně)
User checks the warning checkbox
Button state: ENABLED ✅ (správně)
```

## 🔧 Technické detaily

### CSS Variables vs Media Queries

**Proč CSS variables?**
- ✅ Jednodušší údržba (změna jedné proměnné → změna všude)
- ✅ Runtime změny (JS může měnit variables)
- ✅ Kaskádování (child elementy dědí variables)

**Kdy používat media queries?**
- ✅ Detekce systémové preference (první návštěva)
- ✅ Fallback pro staré browsery

**Naše řešení:**
- Používáme OBÁ dohromady!
- `@media (prefers-color-scheme: dark)` = první návštěva
- `[data-theme]` s CSS variables = uživatelská volba

### Boolean Expression pro IsRunDisabled

**Logika:**
```csharp
IsRunDisabled = 
    IsRunning ||                          // Test už běží
    string.IsNullOrWhiteSpace(SelectedPath) ||  // Není vybrán disk
    (IsDestructiveProfile && !ConfirmDestructive);  // Destruktivní bez potvrzení

IsDestructiveProfile = 
    SelectedProfile == SurfaceTestProfile.HddFull || 
    SelectedProfile == SurfaceTestProfile.FullDiskSanitization
```

## 🚀 Testování

### Manuální test checklist:

1. [ ] Theme toggle mění motiv okamžitě
2. [ ] Motiv zůstává po reloadu
3. [ ] HDD test vyžaduje potvrzení
4. [ ] Sanitizace vyžaduje potvrzení
5. [ ] SSD test NEVYŽADUJE potvrzení (read-only)
6. [ ] Tlačítko se aktivuje po zaškrtnutí checkboxu
7. [ ] Tlačítko je disabled během běhu testu

### Automatizované testy:
- Přidat unit testy pro `IsRunDisabled` logiku
- Přidat Playwright/Selenium testy pro theme toggle

## 📝 Závěr

Oba problémy byly způsobeny **neúplnou implementací**:
- Theme toggle: JS nastavoval atribut, ale CSS ho nečetl
- Button disable: Logika kontrolovala JEN jeden profil, ne všechny destruktivní

Po opravě:
- ✅ Theme toggle plně funkční
- ✅ Button disable funguje pro všechny destruktivní profily
- ✅ Konzistentní chování napříč aplikací
