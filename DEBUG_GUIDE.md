# Debug Guide - Theme & Button Issues

## 🐛 Problémy které řešíme

1. **Theme Toggle nefunguje** - kliknutí nemění motiv
2. **Tlačítko "Spustit test" je stále disabled** i po zaškrtnutí checkboxu

## ✅ Co jsem opravil

### 1. Theme Toggle
- Přidal inicializační script do `_Host.cshtml` který nastaví motiv PŘED startem Blazor
- Přidal `!important` k `body` background a color pro vyšší prioritu než `@media`
- Script loguje do console: "Theme initialized: dark/light"

### 2. Button Enable
- Přidal `@bind:event="onchange"` k checkboxu pro okamžitou aktualizaci
- Logika `IsRunDisabled` už byla správně opravená

## 🔍 Jak debugovat

### Krok 1: Vymaž všechny cached buildy
```powershell
# Zastav aplikaci (Ctrl+C)
dotnet clean
Remove-Item -Recurse -Force .\DiskChecker.Web\bin\
Remove-Item -Recurse -Force .\DiskChecker.Web\obj\
dotnet build
```

### Krok 2: Vyčisti browser
**Chrome/Edge:**
1. F12 (Developer Tools)
2. Pravý klik na Refresh button
3. "Empty Cache and Hard Reload"

**Firefox:**
1. Ctrl+Shift+Delete
2. Vyber "Cache"
3. Vymaž
4. Restart browser

### Krok 3: Zkontroluj console
**Otevři Developer Tools (F12) a zkontroluj:**

1. **Console tab** - mělo by být:
```
Theme initialized: dark  (nebo light)
```

2. **Pokud není:**
```javascript
// Spusť ručně v console:
localStorage.getItem('darkMode')
// Mělo by vrátit: "True" nebo "False" nebo null

// Nastav ručně:
localStorage.setItem('darkMode', 'True')
document.documentElement.setAttribute('data-theme', 'dark')
```

3. **Elements tab** - zkontroluj:
```html
<html lang="cs" data-theme="dark">  <!-- nebo light -->
```

### Krok 4: Zkontroluj Blazor connection
**V Console tab by mělo být:**
```
[Blazor] Connected to the server
```

**Pokud není:**
- Aplikace není správně spuštěná jako admin
- Port je obsazený
- Firewall blokuje

### Krok 5: Zkontroluj checkbox binding
**V Console tab spusť:**
```javascript
// Po kliknutí na checkbox:
document.querySelector('input[type="checkbox"]').checked
// Mělo by vrátit: true
```

## 🧪 Test Checklist

### Theme Toggle Test:
1. [ ] Otevři aplikaci
2. [ ] F12 → Console → mělo by být "Theme initialized: ..."
3. [ ] Klikni na "🌙 Tmavý motiv" / "☀️ Světlý motiv"
4. [ ] Motiv se změní okamžitě
5. [ ] F5 (refresh) → motiv zůstane
6. [ ] Otevři v novém tabu → motiv zůstane

### Button Enable Test:
1. [ ] Otevři "/surface-test"
2. [ ] Vyber disk
3. [ ] Vyber profil "⚠️ Kompletní vymazání disku"
4. [ ] Checkbox není zaškrtnutý → tlačítko DISABLED ❌
5. [ ] Zaškrtni checkbox → tlačítko ENABLED ✅
6. [ ] Odškrtni checkbox → tlačítko DISABLED ❌

## 🔧 Časté problémy

### Problém: Theme se změní, ale vrátí zpět
**Příčina:** `@media (prefers-color-scheme: dark)` má vyšší prioritu

**Řešení:** 
- Zkontroluj že `body` má `!important` v CSS
- Reload aplikace s Ctrl+F5

### Problém: Checkbox se zaškrtne, ale tlačítko zůstane disabled
**Příčina:** Blazor nere-renderuje komponentu

**Možné řešení:**
```csharp
// V SurfaceTest.razor, přidej do OnChange:
private void OnConfirmDestructiveChanged(bool value)
{
    ConfirmDestructive = value;
    StateHasChanged();  // Force re-render
}
```

A změň binding:
```razor
<input type="checkbox" 
       @bind="ConfirmDestructive" 
       @bind:event="onchange"
       @onchange="@((e) => OnConfirmDestructiveChanged((bool)e.Value))"
       disabled="@IsRunning" />
```

### Problém: Console error "Cannot read property 'setAttribute' of null"
**Příčina:** `document.documentElement` není dostupný při spuštění

**Řešení:** Script v `_Host.cshtml` je ve špatném pořadí - musí být PŘED `blazor.server.js`

### Problém: "Theme initialized" se nezobrazuje
**Příčina:** 
1. Cache - browser používá starý `_Host.cshtml`
2. Script syntax error

**Řešení:**
1. Hard reload (Ctrl+F5)
2. Zkontroluj console na syntax errors

## 📊 Expected Behavior

### Správný flow pro Theme Toggle:
```
1. Page Load
   └─> Initialization Script runs
       └─> Check localStorage
           ├─> Found: Apply stored theme
           └─> Not found: Check system preference
               └─> Apply system theme

2. User clicks toggle
   └─> ToggleDarkModeAsync() in ThemeToggle.razor
       ├─> IsDarkMode = !IsDarkMode
       ├─> localStorage.setItem('darkMode', value)
       └─> ApplyThemeAsync()
           └─> document.documentElement.setAttribute('data-theme', ...)
```

### Správný flow pro Button Enable:
```
1. User selects "Kompletní vymazání disku"
   └─> SelectedProfile changes
       └─> Blazor re-renders
           └─> @if condition shows checkbox

2. Checkbox is shown
   └─> ConfirmDestructive = false
       └─> IsRunDisabled = true
           └─> Button is DISABLED

3. User checks checkbox
   └─> @bind fires
       └─> ConfirmDestructive = true
           └─> Blazor re-renders
               └─> IsRunDisabled = false
                   └─> Button is ENABLED
```

## 🚀 Finální kroky

1. **Zastav aplikaci** (Ctrl+C v terminálu)
2. **Vyčisti build:**
   ```powershell
   dotnet clean
   dotnet build
   ```
3. **Spusť jako Admin:**
   ```powershell
   dotnet run --project .\DiskChecker.Web\
   ```
4. **Otevři browser:**
   - Vymaž cache (Ctrl+Shift+Delete)
   - Otevři http://localhost:5128
   - F12 → Console → zkontroluj "Theme initialized"
5. **Testuj podle checklistu výše**

## 📞 Pokud stále nefunguje

Pošli screenshot s:
1. F12 → Console tab (všechny zprávy)
2. F12 → Elements tab (zvýrazni `<html>` element a ukáž atributy)
3. Screenshot checkboxu a tlačítka (včetně stavu disabled/enabled)

A odpověz na tyto otázky:
- Zobrazuje se "Theme initialized" v console? ANO/NE
- Má `<html>` atribut `data-theme`? ANO/NE
- Co vrací `localStorage.getItem('darkMode')` v console? (spusť příkaz)
- Je checkbox zaškrtnutý? ANO/NE
- Co říká browser když najedeš myší na tlačítko? (tooltip nebo disabled state)
