# DiskChecker Enterprise Development Roadmap

**Last Updated:** 2025-02-26  
**Status:** Phase 1 Complete - Console UI Functional  
**Organization:** Česká Pošta (Internal + Open Source)  
**Use Case:** Postal Service Disk Asset Management & Tracking

---

## 🎯 Vision Statement

**DiskChecker** is an enterprise-grade disk diagnostics and asset management system supporting Česká Pošta's distributed infrastructure with:

- **Local-First:** Console + Web UI for disk testing and validation
- **Distributed Tracking:** Central database tracking disk lifecycle across locations (Ostrava → Praha → Brno)
- **Enterprise Integration:** Multi-site deployment, optional cloud sync, compliance auditing
- **Open Source:** Community contributions welcome

---

## ✅ Phase 1: Completed (Console UI - Windows)

✅ SMART diagnostics with metadata collection  
✅ Disk listing with precise capacity info  
✅ FullDiskSanitization test with delta method  
✅ Security & admin enforcement  
✅ Spectre.Console UI with color-coded output  

---

## 🌍 Phase 6: Localization & Multi-Language Support

### Objectives
- Czech (CZ) primary language
- English (EN) for global community & open source
- Foundation for additional languages (DE, SK future)
- **Use Česká Pošta AJIS NuGet library**

### Implementation Plan

#### 6.1 AJIS Localization Library Integration
```csharp
// Install: dotnet add package AjisLocalization (or equivalent)
// Czech Postal Service standard for localization

public interface ILocalizationService
{
    string Get(string key, params object[] args);
    void SetLanguage(string languageCode);  // "cs", "en"
    List<string> AvailableLanguages { get; }
}
```

#### 6.2 File Structure
```
DiskChecker.Core/Localization/
├── ILocalizationService.cs
├── LocalizationService.cs
├── Strings.cs (key definitions)
└── Resources/
    ├── Strings.cs.json (Czech)
    ├── Strings.en.json (English)
    └── Strings.de.json (German - future)
```

#### 6.3 JSON Format
```json
{
  "COMMON": {
    "OK": "OK",
    "ERROR": "Chyba",
    "WARNING": "Varování"
  },
  "DISK_TESTS": {
    "TITLE_CZ": "Povrchové testy",
    "TITLE_EN": "Surface Tests",
    "RUNNING": "Běžící test na {0}...",
    "COMPLETE": "Test dokončen za {0} minut"
  },
  "SMART": {
    "HEALTH_GOOD": "Disk je v pořádku",
    "HEALTH_WARNING": "Disk má varování",
    "POWER_ON_HOURS": "Provozní hodiny: {0}h ({1} let)"
  }
}
```

#### 6.4 Implementation Tasks
- [ ] Create ILocalizationService interface
- [ ] Generate JSON files (CZ + EN)
- [ ] Implement AJIS library integration
- [ ] Extract all UI strings to localization keys
- [ ] Update Console UI (Spectre.Console)
- [ ] Update Blazor components (@Strings.Get())
- [ ] Configuration: user language preference
- [ ] Unit tests for localization
- [ ] Date/Time formatting (Czech vs English)
- [ ] Number formatting ("," vs ".")

---

## 🔗 Phase 7: Centralized Database & Multi-Location Tracking

### Objectives
- **Track disk lifecycle across Česká Pošta locations**
- Support PostgreSQL/SQL Server backends
- Optional cloud sync for enterprise deployments
- **Local-first with optional centralization**

### Use Case: Disk Journey
```
Ostrava Sorting Center:
  → Disk tested on 2025-02-26
  → Result saved locally + synced to central DB
  → Status: "Healthy"

Central Database (Prague):
  → Records: "Disk SN-12345 tested in Ostrava"
  → Tracks: Location history, test count, current status

Praha Distribution Center (3 days later):
  → Same disk tested
  → Central DB shows: "Previously tested in Ostrava, healthy"
  → Operator has complete disk history

Management Dashboard:
  → "Disk SN-12345 traveled: Ostrava → Praha"
  → Performance trends across locations
  → Predictive maintenance insights
```

### Architecture

#### 7.1 Multi-Database Support
**Configuration (appsettings.json):**
```json
{
  "Database": {
    "Mode": "Hybrid",
    "Local": {
      "Provider": "SQLite",
      "Connection": "Data Source=DiskChecker.db"
    },
    "Central": {
      "Enabled": true,
      "Provider": "PostgreSQL",
      "Connection": "Host=central-db.ceskaposta.cz;Database=DiskChecker_Central;Username=...;Password=..."
    },
    "SyncInterval": 300
  }
}
```

#### 7.2 Dual-Database Pattern
**Local (SQLite):**
- Offline-first operation
- No network dependency
- All test results stored locally
- Complete local history

**Central (PostgreSQL):**
- Multi-location aggregation
- Cross-location disk tracking
- Audit trail for compliance
- Optional feature (can be disabled)

#### 7.3 Replication Service
```csharp
public interface IReplicationService
{
    /// <summary>
    /// Sync local test result to central database
    /// </summary>
    Task<bool> SyncTestResultAsync(SurfaceTestResult result);
    
    /// <summary>
    /// Get complete disk history from central DB
    /// </summary>
    Task<List<DiskTestHistory>> GetDiskHistoryAsync(string serialNumber);
    
    /// <summary>
    /// Get locations where disk was tested
    /// </summary>
    Task<List<LocationInfo>> GetDiskLocationsAsync(string serialNumber);
    
    /// <summary>
    /// Get health status across all locations
    /// </summary>
    Task<DiskHealthSummary> GetDiskHealthSummaryAsync(string serialNumber);
}
```

#### 7.4 Central Database Schema
```sql
-- Tenant isolation (for future multi-customer use)
CREATE TABLE Tenants (
    Id UUID PRIMARY KEY,
    Name VARCHAR(200),
    ApiKey VARCHAR(255),
    CreatedAt TIMESTAMP DEFAULT NOW()
);

-- Location tracking
CREATE TABLE Locations (
    Id UUID PRIMARY KEY,
    TenantId UUID REFERENCES Tenants(Id),
    Name VARCHAR(100),  -- "Ostrava", "Praha", "Brno"
    City VARCHAR(100),
    Country VARCHAR(100),
    CreatedAt TIMESTAMP DEFAULT NOW()
);

-- Disk history across locations
CREATE TABLE DiskHistory (
    Id UUID PRIMARY KEY,
    TenantId UUID REFERENCES Tenants(Id),
    DiskSerialNumber VARCHAR(100) NOT NULL,
    LocationId UUID REFERENCES Locations(Id),
    TestDate TIMESTAMP,
    TestResultId VARCHAR(36),
    Status VARCHAR(50),  -- "Healthy", "Warning", "Failed"
    Model VARCHAR(200),
    Manufacturer VARCHAR(100),
    Capacity BIGINT,
    CreatedAt TIMESTAMP DEFAULT NOW(),
    INDEX idx_serial (DiskSerialNumber),
    INDEX idx_location (LocationId),
    INDEX idx_date (TestDate)
);

-- Location summary (for dashboards)
CREATE TABLE DiskLocationSummary (
    Id UUID PRIMARY KEY,
    TenantId UUID REFERENCES Tenants(Id),
    DiskSerialNumber VARCHAR(100),
    LocationName VARCHAR(100),
    LastSeenDate TIMESTAMP,
    TestCount INT DEFAULT 0,
    HealthStatus VARCHAR(50),
    CurrentTemperature INT,
    PowerOnHours BIGINT,
    UpdatedAt TIMESTAMP DEFAULT NOW()
);
```

#### 7.5 Implementation Tasks
- [ ] Create `IReplicationService` interface
- [ ] Implement PostgreSQL provider (EF Core)
- [ ] Setup central database schema
- [ ] Create background sync service (HostedService)
- [ ] Implement conflict resolution (local vs central)
- [ ] Add replication status monitoring
- [ ] Error handling & retry logic
- [ ] Data privacy considerations
- [ ] Unit tests for replication logic
- [ ] Integration tests with real PostgreSQL

---

## 🔌 Phase 8: Enterprise API Server

### Objectives
- **Unified API for Blazor Web, Console, Avalonia clients**
- REST endpoints + WebSocket for real-time updates
- Role-based access control
- Multi-location support

### API Architecture

#### 8.1 Project Structure
```
DiskChecker.API/ (NEW)
├── Program.cs
├── Controllers/
│   ├── DevicesController.cs
│   ├── TestsController.cs
│   ├── LocationsController.cs
│   ├── ReportsController.cs
│   └── ReplicationController.cs
├── Hubs/
│   └── TestProgressHub.cs (SignalR)
├── Services/ (DI layer)
├── Middleware/
│   ├── AuthenticationMiddleware.cs
│   └── TenantContextMiddleware.cs
└── Filters/
    └── TenantAuthorizationFilter.cs
```

#### 8.2 REST Endpoints

**Device Management:**
```
GET    /api/devices                          - List all devices
GET    /api/devices/{serialNumber}           - Device details + history
GET    /api/devices/{serialNumber}/history   - Complete test history
GET    /api/devices/{serialNumber}/locations - Where was disk tested
POST   /api/devices/{serialNumber}/test      - Start new test
GET    /api/devices/{serialNumber}/health    - Current health summary
```

**Test Results:**
```
GET    /api/tests/{testId}                   - Single test result
GET    /api/tests/disk/{serialNumber}        - All tests for disk
GET    /api/tests/location/{locationId}      - Tests at location
POST   /api/tests/{testId}/export            - Export result (PDF/CSV)
```

**Locations:**
```
GET    /api/locations                        - All locations
GET    /api/locations/{id}/devices           - Devices at location
GET    /api/locations/{id}/stats             - Location statistics
```

**Multi-Location:**
```
GET    /api/reports/disk-journey             - Track disk across locations
GET    /api/reports/health-summary           - Organization-wide health
GET    /api/reports/location-comparison      - Compare locations
```

**Replication (Admin):**
```
POST   /api/replication/sync                 - Manual sync trigger
GET    /api/replication/status               - Sync status
GET    /api/replication/pending              - Pending syncs
```

#### 8.3 SignalR Hub for Real-Time Updates
```csharp
public class TestProgressHub : Hub
{
    // Client subscribes to test progress
    public async Task SubscribeToTest(string testId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"test-{testId}");
    }
    
    // Server broadcasts updates (from test service)
    public async Task BroadcastProgress(string testId, SurfaceTestProgress progress)
    {
        await Clients.Group($"test-{testId}").SendAsync("ProgressUpdated", progress);
    }
    
    // Client receives: percentage, speed, ETA
    // UI updates real-time without polling
}
```

**Client Usage (Blazor):**
```csharp
@inject HubConnection hubConnection

// Subscribe to test progress
hubConnection.On<SurfaceTestProgress>("ProgressUpdated", update =>
{
    ProgressPercentage = update.PercentComplete;
    CurrentSpeed = update.CurrentThroughputMbps;
    ETA = CalculateETA(update);
    StateHasChanged();
});
```

#### 8.4 Authentication & Authorization
```csharp
// JWT Token-based authentication
public record AuthRequest(string Username, string Password);
public record AuthResponse(string Token, string RefreshToken);

// Roles
public enum Role { Admin, LocationManager, Operator, Viewer }

// Claims-based authorization
[Authorize(Roles = "Admin,LocationManager")]
[HttpPost("/api/tests/{testId}/export")]
public async Task<IActionResult> ExportTest(string testId) { ... }
```

#### 8.5 Implementation Tasks
- [ ] Create ASP.NET Core Web API project
- [ ] Setup authentication middleware (JWT)
- [ ] Implement REST controllers
- [ ] Setup SignalR hub
- [ ] Add tenant context middleware
- [ ] Database access layer (EF Core)
- [ ] Error handling & global exception handler
- [ ] API documentation (Swagger/OpenAPI)
- [ ] Rate limiting (for public endpoints)
- [ ] CORS policy configuration
- [ ] Unit tests for controllers
- [ ] Integration tests with real DB

---

## 🏢 Phase 9: Multi-Tenant Enterprise Deployment

### Objectives
- Support multiple Česká Pošta branches/regions
- Isolated data per tenant
- Centralized admin console
- Subscription tiers (Free, Standard, Enterprise)

### Implementation Plan

#### 9.1 Tenant Isolation
```sql
-- Add TenantId to all tables
ALTER TABLE SurfaceTestResults ADD TenantId UUID NOT NULL;
ALTER TABLE Devices ADD TenantId UUID NOT NULL;
ALTER TABLE DiskHistory ADD TenantId UUID NOT NULL;

-- Ensure tenant isolation in queries
-- SELECT * FROM SurfaceTestResults WHERE TenantId = @currentTenantId
```

#### 9.2 Deployment Models
- **SaaS:** Hosted on Česká Pošta infrastructure (Prague)
- **On-Premises:** Self-hosted (local PostgreSQL + API)
- **Hybrid:** Local testing + optional sync to Česká Pošta

#### 9.3 Configuration Per Deployment
```json
{
  "Deployment": {
    "Model": "Hybrid",
    "OrganizationName": "Česká Pošta - Ostrava",
    "TenantId": "ceska-posta-ostrava",
    "Capabilities": {
      "LocalTesting": true,
      "CentralSync": true,
      "MultiLocationTracking": true,
      "AdvancedReporting": true
    }
  }
}
```

#### 9.4 Tasks
- [ ] Implement ITenantResolver service
- [ ] Add TenantId to DbContext queries
- [ ] Create tenant management UI (admin)
- [ ] Setup audit logging per tenant
- [ ] Implement subscription level checks
- [ ] Multi-tenant testing strategy

---

## 💼 Phase 10: Enterprise Features & Production Hardening

### Objectives
- Production-ready enterprise deployment
- Security hardening per Česká Pošta standards
- Monitoring & alerting
- Compliance & auditing

### Implementation Plan

#### 10.1 Security Hardening
- [ ] SSL/TLS certificate management
- [ ] Data encryption at rest (PostgreSQL)
- [ ] API key rotation
- [ ] Rate limiting on endpoints
- [ ] SQL injection prevention (EF Core parameterized)
- [ ] XSS protection (Blazor built-in)
- [ ] CORS policy configuration
- [ ] HTTPS enforcement

#### 10.2 Monitoring & Alerting
- [ ] Application Insights integration
- [ ] Health check endpoints
- [ ] Database performance monitoring
- [ ] Error rate thresholds
- [ ] Email alerts for failures
- [ ] Log aggregation (ELK or Azure Monitor)

#### 10.3 Backup & Disaster Recovery
- [ ] PostgreSQL automated backups
- [ ] Backup encryption
- [ ] Disaster recovery drills
- [ ] Data retention policies
- [ ] Point-in-time recovery

#### 10.4 Performance
- [ ] Database query optimization
- [ ] Connection pooling
- [ ] Caching strategy (Redis optional)
- [ ] API response compression
- [ ] Load testing

#### 10.5 Documentation
- [ ] Deployment guide (Docker, Kubernetes optional)
- [ ] Administrator manual
- [ ] API documentation (Swagger)
- [ ] Troubleshooting guide
- [ ] Security hardening checklist
- [ ] Disaster recovery runbook

---

## 📊 Suggested Implementation Timeline

| Phase | Scope | Timeline | Priority | Dependencies |
|-------|-------|----------|----------|-------------|
| 1 | Console UI | Done ✅ | Critical | - |
| 2 | Linux Port | 2-3 weeks | High | Phase 1 |
| **6** | **Localization** | **2 weeks** | **High** | Phase 1 |
| 3 | Blazor Web UI | 3-4 weeks | High | Phase 2 |
| 4 | Avalonia GUI | 4-5 weeks | Medium | Phase 2, 3 |
| 5 | Installation | 2-3 weeks | Medium | Phase 4 |
| **7** | **Central DB** | **4-6 weeks** | **CRITICAL** | Phase 3 |
| **8** | **Enterprise API** | **3-4 weeks** | **CRITICAL** | Phase 7 |
| 9 | Multi-Tenant | 3-4 weeks | High | Phase 8 |
| 10 | Hardening | Ongoing | High | All |

### Why Phase 6, 7, 8 are Critical
- **Phase 6:** Required for global community contributions
- **Phase 7:** Essential for Česká Pošta's multi-location tracking use case
- **Phase 8:** Unified API for all client types (Web, Console, Desktop)

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Clients                              │
├──────────────────┬──────────────────┬──────────────────┤
│  Console UI      │  Blazor Web UI   │  Avalonia GUI    │
│  (Local/Remote)  │  (Browser)       │  (Desktop)       │
└─────────┬────────┴────────┬─────────┴────────┬─────────┘
          │                 │                  │
          └─────────────────┼──────────────────┘
                            │
          ┌─────────────────▼──────────────────┐
          │   Enterprise API Server            │
          │  (REST + SignalR WebSocket)        │
          │  - Authentication (JWT)            │
          │  - Multi-tenant routing            │
          │  - Rate limiting                   │
          └─────────────────┬──────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
   ┌────▼─────┐      ┌─────▼──────┐     ┌─────▼─────┐
   │ Local DB │      │ Central DB  │     │ Replication
   │(SQLite)  │      │(PostgreSQL) │     │ Service
   │Offline-1st      │Multi-tenant │     │(Background)
   └──────────┘      │Centralized  │     └───────────┘
                     └─────────────┘
```

---

## 📞 Key Contacts & Resources

**Repository:** https://github.com/afrowaveltd/DiskChecker  
**Organization:** Česká Pošta (Internal + Open Source)  
**Maintainers:** (TBD)

**Key Technologies:**
- **.NET 10** - Latest framework
- **C# 14** - Language features
- **Blazor Server** - Real-time web UI
- **Entity Framework Core** - Data access
- **PostgreSQL / SQL Server** - Enterprise databases
- **SignalR** - WebSocket communication
- **AJIS** - Česká Pošta localization standard
- **Avalonia** - Cross-platform GUI
- **xUnit + NSubstitute** - Testing

---

## 📝 Change Log

| Date | Phase | Status | Notes |
|------|-------|--------|-------|
| 2025-02-26 | 1 | ✅ Complete | Console UI: SMART metadata, disk tracking, delta method |
| 2025-02-26 | 6-10 | 📋 Planned | Localization, Central DB, API, Multi-tenant, Enterprise |

---

**Happy coding! 🚀**  
**Česká Pošta - Automated Disk Diagnostics & Asset Tracking System**
