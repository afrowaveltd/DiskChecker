# DiskChecker v2.0 - Client-Server Architecture Roadmap

## 🎯 Vize

**Transformace z monolitické desktop aplikace na distribuovaný client-server systém s plnou offline podporou.**

## 🏗️ Architektura

### High-Level Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    DiskChecker v2.0                          │
└─────────────────────────────────────────────────────────────┘

┌──────────────────────── SERVER ────────────────────────────┐
│  DiskChecker.Server (ASP.NET Core)                          │
│  ┌────────────────────────────────────────────────────┐    │
│  │  API Layer                                          │    │
│  │  ├─ SignalR Hubs (real-time)                       │    │
│  │  │   └─ TestProgressHub                            │    │
│  │  │   └─ NotificationHub                            │    │
│  │  ├─ REST API Controllers                           │    │
│  │  │   └─ /api/tests (CRUD)                          │    │
│  │  │   └─ /api/disks (discovery)                     │    │
│  │  │   └─ /api/reports (export)                      │    │
│  │  └─ Authentication (JWT tokens)                    │    │
│  └────────────────────────────────────────────────────┘    │
│  ┌────────────────────────────────────────────────────┐    │
│  │  Business Logic (reuse v1.0 services)              │    │
│  │  └─ DiskChecker.Application                        │    │
│  │  └─ DiskChecker.Core                               │    │
│  └────────────────────────────────────────────────────┘    │
│  ┌────────────────────────────────────────────────────┐    │
│  │  Data Layer                                         │    │
│  │  ├─ PostgreSQL (production)                        │    │
│  │  └─ SQLite (development)                           │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                        ↑ ↓ HTTP/SignalR
┌──────────────────────── CLIENTS ──────────────────────────┐
│  DiskChecker.Client (Multi-platform)                       │
│  ┌────────────────────────────────────────────────────┐   │
│  │  UI Layer                                           │   │
│  │  ├─ Terminal UI (Spectre.Console)                  │   │
│  │  ├─ Desktop UI (Avalonia/WinUI)                    │   │
│  │  └─ Web UI (optional, v2.1+)                       │   │
│  └────────────────────────────────────────────────────┘   │
│  ┌────────────────────────────────────────────────────┐   │
│  │  Client Services                                    │   │
│  │  ├─ Server Communication (HTTP/SignalR)            │   │
│  │  ├─ Offline Mode Manager                           │   │
│  │  └─ Data Sync Service                              │   │
│  └────────────────────────────────────────────────────┘   │
│  ┌────────────────────────────────────────────────────┐   │
│  │  Local Storage (Offline Mode)                      │   │
│  │  └─ SQLite Database                                │   │
│  └────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Projekt struktura

```
DiskChecker/
├── src/
│   ├── DiskChecker.Core/              # Shared domain models (reuse v1.0)
│   ├── DiskChecker.Application/       # Business logic (reuse v1.0)
│   ├── DiskChecker.Infrastructure/    # Hardware access (reuse v1.0)
│   │
│   ├── DiskChecker.Server/            # 🆕 Server API
│   │   ├── Controllers/
│   │   │   ├── TestsController.cs
│   │   │   ├── DisksController.cs
│   │   │   └── ReportsController.cs
│   │   ├── Hubs/
│   │   │   ├── TestProgressHub.cs
│   │   │   └── NotificationHub.cs
│   │   ├── Services/
│   │   │   ├── TokenService.cs
│   │   │   └── UserManagementService.cs
│   │   └── Program.cs
│   │
│   ├── DiskChecker.Client.Core/       # 🆕 Shared client logic
│   │   ├── Services/
│   │   │   ├── ServerApiClient.cs
│   │   │   ├── OfflineModeManager.cs
│   │   │   └── DataSyncService.cs
│   │   └── ViewModels/
│   │
│   ├── DiskChecker.Client.Terminal/   # 🆕 Terminal UI (upgrade v1.0)
│   │   └── Program.cs
│   │
│   ├── DiskChecker.Client.Desktop/    # 🆕 Desktop UI
│   │   ├── Views/
│   │   └── Program.cs
│   │
│   └── DiskChecker.Client.Web/        # 🆕 Web UI (optional, v2.1+)
│       └── Program.cs
│
└── tests/
    ├── DiskChecker.Server.Tests/
    └── DiskChecker.Client.Tests/
```

## 🎭 Role & Permissions

### Server-Side Roles

| Role | Permissions | Use Case |
|------|-------------|----------|
| **Admin** | Full access | Server management, user management |
| **Technician** | Run tests, view all results | Service desk |
| **User** | Run tests, view own results | End user |
| **ReadOnly** | View results only | Manager, auditor |

### Client Modes

#### 1️⃣ Single-User (Offline)
```
DiskChecker.Client.Terminal.exe
    ├─ No server connection
    ├─ Local SQLite database
    ├─ Full test functionality
    ├─ Export results (JSON/CSV/PDF)
    └─ Import results for comparison
```

**Features:**
- ✅ All disk tests (SMART, Surface, etc.)
- ✅ Local database for history
- ✅ Export/Import for sharing
- ✅ No authentication needed
- ❌ No cloud storage
- ❌ No team collaboration

#### 2️⃣ Multi-User (Online)
```
DiskChecker.Client.Terminal.exe --server https://api.diskchecker.com
    ├─ Token-based authentication
    ├─ Server database
    ├─ Full test functionality
    ├─ Automatic result sync
    └─ Team collaboration features
```

**Features:**
- ✅ All disk tests
- ✅ Cloud storage (server DB)
- ✅ Test result comparison across users
- ✅ Real-time notifications
- ✅ Team dashboard
- ✅ Role-based access control

## 🔐 Authentication & Security

### Token-Based Auth (JWT)

```csharp
// Client login flow:
1. User: Login with credentials
2. Server: Validate & issue JWT token
3. Client: Store token securely
4. Client: Include token in all requests
5. Server: Validate token & authorize

// Token structure:
{
  "sub": "user@example.com",
  "role": "Technician",
  "exp": 1234567890,
  "client_id": "terminal-v2.0"
}
```

### API Endpoints

```
POST   /api/auth/login          # Login (get token)
POST   /api/auth/refresh        # Refresh token
POST   /api/auth/logout         # Logout

GET    /api/disks               # List available disks
POST   /api/tests/start         # Start new test
GET    /api/tests/{id}          # Get test result
GET    /api/tests               # List all tests (paginated)
DELETE /api/tests/{id}          # Delete test

POST   /api/reports/export      # Export test result
GET    /api/reports/compare     # Compare tests

# SignalR Hubs:
/hubs/test-progress             # Real-time test progress
/hubs/notifications             # System notifications
```

## 📡 Communication Protocol

### REST API (Request/Response)

```csharp
// Example: Start test
POST /api/tests/start
{
    "diskPath": "D:",
    "profile": "HDD_FULL",
    "requestId": "uuid-1234"
}

// Response:
{
    "testId": "test-5678",
    "status": "Started",
    "estimatedDuration": "2h 30m"
}
```

### SignalR (Real-Time Updates)

```javascript
// Client subscribes to test progress:
connection.invoke("SubscribeToTest", "test-5678");

// Server pushes progress updates:
connection.on("ProgressUpdate", (progress) => {
    console.log(`${progress.percent}% complete`);
});

// Server notifies completion:
connection.on("TestCompleted", (result) => {
    console.log("Test finished!", result);
});
```

## 💾 Data Synchronization

### Offline → Online Sync

```
1. User runs test in offline mode
2. Test result saved to local SQLite
3. User connects to server later
4. Client detects pending sync
5. Client uploads results to server
6. Server stores in PostgreSQL
7. Client marks local result as synced
```

### Conflict Resolution

```csharp
// If test already exists on server:
enum ConflictResolution
{
    KeepLocal,      // User's local data wins
    KeepServer,     // Server data wins
    KeepBoth,       // Create duplicate
    Merge          // Smart merge (if possible)
}
```

## 🎨 Client UI Options

### 1. Terminal UI (Spectre.Console)
- **Pros:** Lightweight, cross-platform, scriptable
- **Cons:** Limited visuals
- **Target:** Power users, automation

### 2. Desktop UI (Avalonia)
- **Pros:** Native look, cross-platform (Windows/Linux/Mac)
- **Cons:** Larger binary
- **Target:** Regular users, GUI preference

### 3. Web UI (Optional, v2.1+)
- **Pros:** No installation, accessible anywhere
- **Cons:** Requires server (no offline)
- **Target:** Managers, read-only users

## 🚀 Migration Path (v1.0 → v2.0)

### Phase 1: Core Refactoring
1. Extract `DiskChecker.Core` (no changes needed)
2. Extract `DiskChecker.Application` (no changes needed)
3. Extract `DiskChecker.Infrastructure` (no changes needed)
4. ✅ **These are already well-separated in v1.0!**

### Phase 2: Server Development
1. Create `DiskChecker.Server` project
2. Implement REST API controllers
3. Implement SignalR hubs
4. Add authentication (JWT)
5. Add role-based authorization
6. Database schema (PostgreSQL)

### Phase 3: Client Refactoring
1. Create `DiskChecker.Client.Core` (shared logic)
2. Create `ServerApiClient` service
3. Create `OfflineModeManager` service
4. Upgrade `DiskChecker.Client.Terminal` (reuse v1.0 UI)
5. Create `DiskChecker.Client.Desktop` (new)

### Phase 4: Testing & Deployment
1. Integration tests (client ↔ server)
2. UAC privilege tests (ensure no issues)
3. Offline/Online mode switching tests
4. Performance tests (SignalR under load)
5. Security audit (penetration testing)

## 📅 Timeline Estimate

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| **Phase 1** | 1 week | Core refactoring, project structure |
| **Phase 2** | 3 weeks | Server API, authentication, database |
| **Phase 3** | 3 weeks | Client refactoring, offline/online modes |
| **Phase 4** | 2 weeks | Testing, bug fixes, documentation |
| **Total** | **9 weeks** | v2.0 Release Candidate |

## 🎯 Success Criteria

### Functional Requirements
- ✅ Client can run tests offline
- ✅ Client can connect to server and sync
- ✅ Server stores results centrally
- ✅ Real-time progress updates via SignalR
- ✅ Token-based authentication works
- ✅ Role-based access control enforced
- ✅ Export/Import works in both modes

### Non-Functional Requirements
- ✅ No UAC privilege issues
- ✅ Cross-platform (Windows/Linux)
- ✅ Performance: Handle 100+ concurrent clients
- ✅ Security: Pass basic penetration tests
- ✅ Documentation: API docs, user guides

## 🔄 Backwards Compatibility

### v1.0 Data Migration

```csharp
// Migrate v1.0 SQLite database to v2.0 format:
public class DatabaseMigrator
{
    public async Task MigrateV1ToV2Async(string v1DbPath)
    {
        // 1. Read v1.0 SQLite database
        var v1Tests = await ReadV1TestsAsync(v1DbPath);
        
        // 2. Convert to v2.0 format
        var v2Tests = v1Tests.Select(t => new V2TestRecord
        {
            Id = t.Id,
            // Map fields...
            Version = "v1.0-migrated"
        });
        
        // 3. Import to v2.0 local database
        await SaveToV2DatabaseAsync(v2Tests);
        
        // 4. Optionally sync to server
        if (IsOnline())
        {
            await SyncToServerAsync(v2Tests);
        }
    }
}
```

## 📝 API Documentation

### OpenAPI/Swagger

```csharp
// Program.cs (Server)
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "DiskChecker API",
        Version = "v2.0",
        Description = "Disk testing and monitoring API"
    });
    
    // JWT auth in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
});
```

**Swagger UI:** `https://api.diskchecker.com/swagger`

## 🐳 Deployment Options

### Option 1: Self-Hosted (Windows Server)
```
IIS + Kestrel
└─ DiskChecker.Server.exe
    ├─ PostgreSQL (local)
    └─ Redis (optional, for caching)
```

### Option 2: Docker Container
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ .
EXPOSE 5000
ENTRYPOINT ["dotnet", "DiskChecker.Server.dll"]
```

### Option 3: Cloud (Azure/AWS)
```
Azure App Service
├─ DiskChecker.Server (Web App)
├─ Azure Database for PostgreSQL
└─ Azure SignalR Service (scale-out)
```

## 🔮 Future Enhancements (v2.1+)

1. **Mobile App** (Xamarin/MAUI)
   - View test results on phone
   - Receive push notifications
   - No test execution (read-only)

2. **Web Dashboard**
   - Manager overview
   - Statistics & reports
   - User management UI

3. **AI-Powered Diagnostics**
   - Predict disk failures
   - Recommend maintenance
   - Anomaly detection

4. **Multi-Tenant Support**
   - Separate data per organization
   - Custom branding
   - SaaS model

## ✅ Acceptance Criteria

### For v2.0 Release:

- [ ] Server API fully functional with all endpoints
- [ ] SignalR hubs working for real-time updates
- [ ] JWT authentication implemented
- [ ] Role-based authorization working
- [ ] Terminal client can run offline
- [ ] Terminal client can connect to server
- [ ] Desktop client basic UI implemented
- [ ] Data sync (offline → online) working
- [ ] v1.0 database migration tool working
- [ ] API documentation (Swagger) complete
- [ ] User guide written
- [ ] Deployment guide written
- [ ] All tests passing (90%+ code coverage)
- [ ] Security audit passed
- [ ] Performance benchmarks met

## 📚 Documentation Deliverables

1. **API Documentation**
   - OpenAPI spec (Swagger)
   - Endpoint descriptions
   - Authentication guide
   - Rate limiting policy

2. **User Guides**
   - Getting started (offline mode)
   - Connecting to server
   - Running tests
   - Interpreting results

3. **Admin Guides**
   - Server deployment
   - User management
   - Backup & restore
   - Monitoring & logging

4. **Developer Guides**
   - Architecture overview
   - Contributing guidelines
   - Building from source
   - Creating plugins

## 🎓 Conclusion

v2.0 bude **enterprise-ready** distribuovaný systém s:
- ✅ Plnou offline funkcionalitou
- ✅ Cloud synchronizací
- ✅ Team collaboration
- ✅ Role-based security
- ✅ Real-time updates
- ✅ Cross-platform support

**A nejdůležitější:**
- ✅ ŽÁDNÉ UAC problémy! 🎉
