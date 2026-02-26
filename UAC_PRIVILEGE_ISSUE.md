# UAC Privilege Issue - Root Cause & Solution

## 🐛 Problem Discovery

**Observed Behavior:**
- Application runs WITHOUT admin → UI works perfectly ✅ (but disk tests fail)
- Application runs WITH admin → UI breaks ❌ (theme toggle, button enable fail)

**Root Cause:**
Windows UAC (User Access Control) creates a **security boundary** between processes running with different privilege levels:
- Application process: **Administrator**
- Browser process: **Normal User**

This boundary blocks:
- ❌ Blazor SignalR communication
- ❌ JavaScript Interop (localStorage, DOM manipulation)
- ❌ All JS ↔ C# calls

## 📊 Technical Explanation

### Windows UAC Architecture

```
┌─────────────────────────────────────────────────────┐
│  Browser (Normal User)                              │
│  ┌────────────────────────────────────────────┐    │
│  │  JavaScript Context                        │    │
│  │  - localStorage.setItem()                  │    │
│  │  - document.documentElement.setAttribute() │    │
│  │  - Blazor SignalR connection               │    │
│  └────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
           ↓ ↓ ↓ ↓ ↓ ↓ ↓
      ❌ UAC BLOCKS ❌
           ↑ ↑ ↑ ↑ ↑ ↑ ↑
┌─────────────────────────────────────────────────────┐
│  DiskChecker.Web (Administrator)                    │
│  ┌────────────────────────────────────────────┐    │
│  │  Blazor Server                             │    │
│  │  - SignalR Hub                             │    │
│  │  - JS Interop calls                        │    │
│  │  - Component rendering                     │    │
│  └────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

### Why UAC Blocks Cross-Privilege Communication

1. **Security Isolation:** UAC prevents normal user processes from accessing admin resources
2. **Named Pipes Security:** SignalR uses named pipes which have ACL restrictions
3. **COM Security:** JavaScript Interop may use COM which enforces security contexts
4. **Token Elevation:** Admin processes run with elevated token, normal processes with filtered token

## ✅ Solutions Implemented

### Solution 1: Warning on Application Startup

Added ASCII warning box in `Program.cs`:
```csharp
if (isAdmin)
{
    Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  ⚠️  VAROVÁNÍ: Aplikace běží s ADMIN právy                   ║");
    Console.WriteLine("║  Pro správnou funkci spusťte browser TAKÉ jako admin          ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
}
```

### Solution 2: UI Warning Box

Added warning in `SurfaceTest.razor`:
```razor
@if (!IsRunningAsAdmin && SelectedProfile == SurfaceTestProfile.FullDiskSanitization)
{
    <div class="warning-box">
        <strong>⚠️ VAROVÁNÍ: Nedostatečná oprávnění</strong>
        <p>Aplikace NEBĚŽÍ s administrátorskými právy.</p>
    </div>
}
```

### Solution 3: Privilege Helper Class

Created `PrivilegeHelper.cs` for:
- Detecting current privilege level
- Selective elevation (elevate only when needed)
- Cross-platform support (Windows/Linux)

## 🎯 Recommended Approach

### Option A: Run App WITHOUT Admin (BEST for UI)

```powershell
# Normal startup
dotnet run --project .\DiskChecker.Web\

# Open browser normally
start http://localhost:5128
```

**Pros:**
- ✅ All UI features work perfectly
- ✅ Theme toggle works
- ✅ Button enable/disable works
- ✅ Blazor SignalR connection stable
- ✅ JavaScript Interop reliable

**Cons:**
- ⚠️ Disk sanitization will fail (requires admin)
- ⚠️ Some SMART data might be unavailable

**Solution for disk operations:**
- Use `PrivilegeHelper.RunElevatedAsync()` for specific admin tasks
- Show UAC prompt only when needed
- Most operations (SMART check, read-only tests) work without admin

### Option B: Run Browser as Admin (when needed)

```powershell
# Start app as admin
dotnet run --project .\DiskChecker.Web\

# Start browser as admin (separate terminal)
Start-Process msedge "http://localhost:5128" -Verb RunAs
```

**Pros:**
- ✅ Full disk access
- ✅ All operations work

**Cons:**
- ⚠️ Browser running as admin is security risk
- ⚠️ Extra step for user

## 🔧 Implementation Details

### 1. Admin Detection

```csharp
private bool IsRunningAsAdmin
{
    get
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true; // Linux: assume sudo/root

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

### 2. Selective Elevation

```csharp
public static async Task<int> RunElevatedAsync(string fileName, string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = true,
        Verb = "runas", // Request UAC elevation
        CreateNoWindow = false
    };

    using var process = Process.Start(psi);
    await process.WaitForExitAsync();
    return process.ExitCode;
}
```

### 3. UI Feedback

```razor
<!-- Show warning when insufficient privileges -->
@if (!IsRunningAsAdmin && RequiresAdmin(SelectedProfile))
{
    <div class="warning-box">
        <!-- Warning content -->
    </div>
}
```

## 📊 Comparison Matrix

| Scenario | App Privilege | Browser Privilege | UI Works? | Disk Access? | Recommended? |
|----------|---------------|-------------------|-----------|--------------|--------------|
| Normal/Normal | User | User | ✅ Yes | ❌ Limited | ⭐⭐⭐⭐⭐ BEST |
| Admin/Admin | Admin | Admin | ✅ Yes | ✅ Full | ⭐⭐⭐ OK |
| Admin/Normal | Admin | User | ❌ NO | N/A | ❌ AVOID |
| Normal/Admin | User | Admin | ✅ Yes | ❌ Limited | ❌ AVOID |

## 🚀 Usage Guide

### For Development:
1. Run application **WITHOUT admin**
2. Use normal browser
3. UI works perfectly
4. For disk tests requiring admin:
   - Show UAC prompt
   - User approves elevation
   - Test runs with elevated process

### For Production Deployment:
1. **Desktop App:** Use selective elevation with UAC prompts
2. **Server/Service:** Run service with appropriate service account
3. **Web Server:** Use IIS application pool identity with specific permissions

## 📝 Code Changes Summary

### Files Modified:
1. `DiskChecker.Web\Program.cs` - Added admin detection and warning
2. `DiskChecker.Web\Pages\SurfaceTest.razor` - Added UI warning and admin check
3. `DiskChecker.Infrastructure\Helpers\PrivilegeHelper.cs` - NEW: Helper for privilege management

### New Features:
- ✅ Admin privilege detection
- ✅ Console warning on startup
- ✅ UI warning before admin-required operations
- ✅ Selective elevation support
- ✅ Cross-platform compatibility

## 🐛 Debugging Tips

### How to verify UAC is the issue:

1. **Check Process Tokens:**
```powershell
# In PowerShell as Admin:
Get-Process | Where-Object {$_.ProcessName -like "*DiskChecker*"} | ForEach-Object {
    $process = $_
    try {
        $handle = $process.Handle
        [System.Security.Principal.WindowsIdentity]::new($handle).Groups | Where-Object {
            $_.Value -eq "S-1-16-12288" # High Integrity Level
        }
    } catch {}
}
```

2. **Check Browser Console:**
```javascript
// In browser console (F12):
console.log('Browser integrity:', 
    window.external?.IsWebViewControl ? 'WebView' : 'Normal Browser');
```

3. **Test SignalR Connection:**
```javascript
// Should show connection state
console.log('Blazor connection:', 
    Blazor?._internal?.navigationManager?.uri);
```

## ⚠️ Security Considerations

### Running Browser as Admin - DON'T!
- ❌ Security vulnerability (all web content runs elevated)
- ❌ Malicious websites could exploit admin privileges
- ❌ Browser sandbox is weakened

### Recommended Security Approach:
1. ✅ Run app as normal user
2. ✅ Use UAC prompts for specific admin tasks
3. ✅ Minimize time spent with elevated privileges
4. ✅ Audit all elevated operations
5. ✅ Use Windows service accounts for automated tasks

## 📚 References

- [UAC Process and Interactions](https://docs.microsoft.com/en-us/windows/security/identity-protection/user-account-control/how-user-account-control-works)
- [Blazor SignalR Security](https://docs.microsoft.com/en-us/aspnet/core/blazor/security)
- [Process Privileges and Security](https://docs.microsoft.com/en-us/windows/win32/secauthz/privileges)

## ✅ Final Recommendation

**BEST PRACTICE:**
1. Run DiskChecker.Web **WITHOUT admin privileges**
2. Implement selective elevation for disk operations
3. Show clear warnings when admin is required
4. Prompt for UAC only when necessary
5. Keep browser running as normal user

This approach provides:
- ✅ Maximum security
- ✅ Best user experience
- ✅ Reliable UI functionality
- ✅ Granular privilege control
