# DiskChecker - AI Assistant Context & Handoff Guide

**Last Updated:** 2025-02-26  
**For:** AI Assistants (GitHub Copilot, Claude, etc.)  
**Purpose:** Enable continuity when switching between sessions/assistants

---

## 🎯 Project At a Glance

**DiskChecker** is an enterprise-grade disk diagnostics & asset management system for **Česká Pošta** (Czech Postal Service).

- **Vision:** Free, open-source, transparent tool for disk health monitoring across distributed postal locations
- **Status:** Phase 1 Complete (Console UI fully functional)
- **Tech:** .NET 10, C# 14, Blazor Server, AJIS (custom format)
- **Repository:** https://github.com/afrowaveltd/DiskChecker
- **Organization:** Česká Pošta (Internal + Open Source Community)

---

## 📊 Current Architecture

### **Completed (Phase 1)**
```
DiskChecker.Core/              Domain models, interfaces, business logic
DiskChecker.Application/       Services, business logic orchestration
DiskChecker.Infrastructure/    Hardware access (diskpart, smartctl), persistence
DiskChecker.UI/                Console UI (Windows) - FULLY FUNCTIONAL ✅
DiskChecker.Web/               Blazor Server (experimental, partial)
DiskChecker.Tests/             xUnit + NSubstitute tests
```

### **Key Components**

**SMART Provider (Platform Detection):**
- `WindowsSmartaProvider.cs` - Uses smartctl on Windows
- `LinuxSmartaProvider.cs` - (Partial) Uses native smartctl on Linux
- `SmartaProviderFactory.cs` - Detects OS and returns correct provider

**Surface Testing (Disk Validation):**
- `SequentialFileTestExecutor.cs` - Main test executor
- Write 100MB sequential files + verify checksum
- Delta method for drive letter detection (100% reliable)
- User fallback: mounted drive selection from list

**Storage (Phase 1.5 - NEW DESIGN):**
- **AJIS per-disk format:** `TestResults/DISK_SN.ajis`
- **SQLite index:** `DiskIndex.sqlite3` (routing + metadata)
- **Streaming:** Read/write without loading full file to RAM
- **Archivation:** Move old disks to Archive/ + mark in SQL

---

## 🏗️ Architecture Decisions (CRITICAL!)

### **Storage Strategy: AJIS + SQLite Hybrid**

**Decision:** Option B (One AJIS file per disk serial number)

**Why:**
- Memory efficient (AJIS streams, no full load)
- Filesystem happy (~100 files max, not millions)
- Archive-friendly (move file + SQL flag)
- Git-friendly (AJIS is text-based, diffable)
- Queryable (SQLite index for fast lookups)

**Structure:**
```
Data/TestResults/
├── DiskIndex.sqlite3                    (SQLite routing)
├── DISK_WD123ABC.ajis                   (per-disk AJIS)
├── DISK_ST456DEF.ajis
└── Archive/
    ├── DISK_OLD111.ajis                 (archived, marked in SQL)
    └── DISK_OLD222.ajis
```

**SQLite Schema (DiskIndex.sqlite3):**
```sql
CREATE TABLE DiskIndex (
    SerialNumber VARCHAR(100) PRIMARY KEY,
    Manufacturer VARCHAR(100),
    Model VARCHAR(200),
    Capacity BIGINT,
    ResultsFile VARCHAR(500),              -- "DISK_SN.ajis" path
    IsArchived BOOLEAN DEFAULT 0,
    TestCount INT,
    LastTestedAt DATETIME,
    LastStatus VARCHAR(50)                 -- "Healthy", "Warning", "Failed"
);
```

**AJIS Format (per-disk file):**
```json
{
  metadata: {
    serialNumber: "WD123ABC",
    manufacturer: "Western Digital",
    capacity: 1099511627776
  },
  tests: [
    { testId, timestamp, profile, results, samples, checksum },
    { ... another test ... }
  ]
}
```

**Read/Write Pattern:**
```csharp
// Write: Append new test to AJIS (streaming)
AjisFile.AppendTest("DISK_SN.ajis", testResult);

// Read: Stream tests without loading whole file
var tests = AjisFile.ReadTestsStreaming("DISK_SN.ajis")
    .Where(t => t.Timestamp > since)
    .ToList();

// Index: SQLite for metadata queries
var disk = db.DiskIndex.FirstOrDefault(d => d.SerialNumber == "WD123ABC");
```

**This decision is LOCKED for Phase 1-5. Do NOT change without discussion.**

---

## 🔄 Phased Roadmap (CRITICAL!)

### **Phase 1: Complete ✅**
- Console UI fully functional
- SMART diagnostics with metadata
- FullDiskSanitization test
- Delta method + user fallback
- Comprehensive metadata collection

### **Phase 2: Linux (Next)**
- Port to Linux (smartctl native)
- Cross-platform abstraction
- 2-3 weeks timeline

### **Phase 6: Localization (Quick)**
- Czech/English (AJIS library)
- 2 weeks - can do parallel

### **Phase 3: Blazor Web UI**
- Real-time SignalR progress
- Device dashboard
- Test history visualization

### **Phase 7: Central Database (CRITICAL)**
- PostgreSQL for multi-location tracking
- Disk "journey" across locations (Ostrava → Praha)
- Replication service (AJIS → PostgreSQL)
- 4-6 weeks

### **Phase 8: Enterprise API**
- REST endpoints + WebSocket
- Multi-tenant
- JWT authentication

### **Phase 1.5: AJIS Storage (CURRENT FOCUS)**
- Implement AJIS per-disk storage
- SQLite index
- Streaming read/write
- Migration from EF Core SQLite to AJIS

**See:** `docs/DEVELOPMENT_ROADMAP.md` and `docs/DEVELOPMENT_ROADMAP_EXTENDED.md`

---

## ⚙️ Critical Code Standards

### **XML Documentation (MANDATORY)**
Every public class, method, property MUST have XML docs:
```csharp
/// <summary>
/// Executes surface test on specified drive.
/// </summary>
/// <param name="drive">Drive to test (must not be system drive)</param>
/// <param name="progress">Progress callback (optional)</param>
/// <returns>Test result with metrics and samples</returns>
/// <exception cref="InvalidOperationException">If drive is system drive</exception>
public async Task<SurfaceTestResult> ExecuteAsync(
    CoreDriveInfo drive,
    IProgress<SurfaceTestProgress>? progress = null)
```

### **Testing (xUnit + NSubstitute)**
- New code MUST have unit tests
- Use xUnit test fixtures
- Mock dependencies with NSubstitute
- Prefer permissive licenses (xUnit, NSubstitute are compatible)

Example:
```csharp
public class SmartCheckServiceTests
{
    [Fact]
    public async Task RunAsync_WithHealthyDisk_ReturnsGradeA()
    {
        // Arrange
        var provider = Substitute.For<ISmartaProvider>();
        provider.GetSmartDataAsync(Arg.Any<CoreDriveInfo>())
            .Returns(new SmartaData { /* healthy */ });
        var service = new SmartCheckService(provider, new QualityCalculator());
        
        // Act
        var result = await service.RunAsync(_testDrive);
        
        // Assert
        Assert.Equal(QualityGrade.A, result.Rating.Grade);
    }
}
```

### **Async/Await Throughout**
- All I/O is async (file, network, process)
- Use `CancellationToken` parameter
- Avoid `.Result` and `.Wait()`

### **Error Messages (Czech-First, English-Ready)**
When adding new errors:
```csharp
// WRONG:
throw new InvalidOperationException("Cannot access disk");

// RIGHT: (localizable, descriptive)
throw new InvalidOperationException(
    $"Disk {drive.Path} cannot be accessed. " +
    $"Check permissions (admin required) or device disconnection. " +
    $"Error: {ex.Message}");
```

### **Dependencies (Check Licenses!)**
Before adding NuGet packages:
- Check license compatibility (Czech Pošta internal use)
- Prefer MIT, Apache 2.0, BSD, or public domain
- AVOID: GPL (viral license)
- Document in `Directory.Build.props`

**Current approved:**
- xUnit (MIT)
- NSubstitute (BSD-3-Clause)
- Spectre.Console (MIT)
- Newtonsoft.Json (MIT)
- Entity Framework Core (MIT)
- smartctl (CLI tool, no license issue)

---

## 🔑 Key Files & When to Modify

| File | Purpose | When to Touch |
|------|---------|---------------|
| `DiskChecker.Core/Models/SurfaceTestModels.cs` | Test result schema | Adding new test metadata |
| `DiskChecker.Infrastructure/Hardware/SequentialFileTestExecutor.cs` | Main test logic | Improving test algorithm |
| `DiskChecker.UI/Console/MainConsoleMenu.cs` | Console UI | Adding menu options |
| `docs/DEVELOPMENT_ROADMAP.md` | Phase 1-5 plan | After completing a phase |
| `docs/DEVELOPMENT_ROADMAP_EXTENDED.md` | Phase 6-10 plan | After completing Phase 1 |
| `docs/AI_CONTEXT.md` | THIS FILE | Every session end! |
| `DiskChecker.Infrastructure/Hardware/WindowsSmartaProvider.cs` | SMART data parsing | If smartctl output changes |

---

## 🚀 How to Start/Handoff

### **First Session (or after handoff):**
1. **Read this file** (you're doing it!)
2. **Read `DEVELOPMENT_ROADMAP.md`** - understand phases
3. **Check `docs/IMPLEMENTATION_COMPLETED.md`** - what's done
4. **Review recent commits** - understand latest context
5. **Build & test:** `dotnet build && dotnet test`

### **Before Ending Session:**
1. **Update `docs/AI_CONTEXT.md`** (this file):
   - Add what you accomplished
   - Note any blockers/decisions
   - Update "Last Updated" date
2. **Update `DEVELOPMENT_ROADMAP.md`**:
   - Mark phases complete/in-progress
   - Add new findings
   - Document architectural decisions
3. **Commit with clear message:**
   ```bash
   git add docs/AI_CONTEXT.md docs/DEVELOPMENT_ROADMAP.md
   git commit -m "chore: update context and roadmap after session

   - Completed: [what you did]
   - In Progress: [what's next]
   - Key Decision: [if any]
   - Next AI Session: [what to focus on]"
   ```

---

## 🛠️ Debugging Checklist

**When something breaks:**

1. **Check admin/sudo:** Windows tests need admin, Linux needs sudo
2. **Check smartctl:** 
   - Windows: `smartctl --version` (must be in PATH)
   - Linux: `smartctl --scan`
3. **Check diskpart:** Windows only, requires admin
4. **Check DriveLetter logic:** See `SequentialFileTestExecutor.ExecuteAsync()`
5. **Check file permissions:** Write access to test directory needed
6. **Check AJIS:** Verify AJIS NuGet package is installed
7. **Run with Debug output:**
   ```csharp
   System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
   ```

---

## 📋 Session Template

**Copy & paste at the end of EACH session:**

```markdown
## Session [DATE] - [AI Name]

### What I Did:
- [ ] Task 1
- [ ] Task 2

### What I Learned:
- Key insight 1
- Key insight 2

### Blockers:
- [ ] Blocker 1 (if any)

### Next Session Should:
- Focus on: Phase X, Task Y
- Check: [specific file/issue]
- Remember: [context note]

### Files Modified:
- `path/to/file1.cs`
- `path/to/file2.md`

### Build Status:
- [x] Builds successfully
- [x] Tests pass
- [ ] Issues: (describe)

### Commit Hash:
```

---

## 📞 How Decisions Get Made

**Architectural Decision Process:**

1. **Proposal:** Document in issue or PR comment
2. **Discussion:** Consider pros/cons (use matrix if complex)
3. **Decision:** Agreed by maintainer(s)
4. **Documentation:** Add to `DEVELOPMENT_ROADMAP_EXTENDED.md`
5. **Implementation:** Code changes
6. **Verification:** Tests pass, no regressions

**Example:** Storage strategy (AJIS per-disk) was decided because:
- ✅ Memory efficient (streaming)
- ✅ Filesystem healthy (~100 files)
- ✅ Git-friendly (text-based)
- ❌ Not: Full document (too slow)
- ❌ Not: One file per test (filesystem chaos)

---

## 🎓 Important Concepts for New AI Assistants

### **Delta Method for Drive Detection**
When formatt disk with diskpart:
1. Get available drive letters BEFORE (`DrivesBefore`)
2. Run diskpart, format, assign
3. Get available drive letters AFTER (`DrivesAfter`)
4. New drive = `DrivesAfter - DrivesBefore`

This is 100% reliable and avoids PowerShell Get-Volume issues.

### **User Fallback for Mounted Disks**
If delta fails:
1. Show user list of non-system mounted drives
2. User selects disk from list (DriveInfo enumeration)
3. Continue test with selected drive
4. Safe because user confirms explicitly

### **SMART Attribute Mapping**
- ID 5: Reallocated sectors
- ID 9: Power-on hours
- ID 194: Temperature (HDA Temp)
- ID 190: Airflow Temperature
- ID 3: Spin speed (RPM)

---

## 🚨 Do NOT Ever

1. ❌ Write to system drive (C:) during testing - check first!
2. ❌ Forget XML documentation - it's mandatory
3. ❌ Use blocking `.Wait()` or `.Result` - async everywhere
4. ❌ Add GPL dependencies - license issue for Czech Pošta
5. ❌ Commit secrets (passwords, API keys) - use `appsettings.json`
6. ❌ Forget to update roadmap after session
7. ❌ Change storage strategy without recording decision
8. ❌ Skip tests for "quick" changes - tests are non-negotiable

---

## ✅ Do Always

1. ✅ Read this file at session start
2. ✅ Update roadmap & this file at session end
3. ✅ Write XML docs for all public APIs
4. ✅ Write tests for new functionality
5. ✅ Check admin/sudo requirements for platform
6. ✅ Use `CancellationToken` for async operations
7. ✅ Commit with clear messages
8. ✅ Build & test before pushing

---

## 🗺️ Recommended Session Path

**If you have 1 hour:**
1. (5 min) Read this file
2. (10 min) Check DEVELOPMENT_ROADMAP.md for next task
3. (40 min) Implement next Phase 2 Linux task
4. (5 min) Update this file + roadmap, commit

**If you have 4 hours:**
1. (10 min) Read this file
2. (15 min) Review last session notes
3. (3 hours) Complete Phase 2 or Phase 6 tasks
4. (15 min) Update docs, write session notes, commit

**If you're new to project:**
1. (30 min) Read AI_CONTEXT.md + DEVELOPMENT_ROADMAP.md
2. (30 min) Clone repo, build, run tests
3. (30 min) Read SequentialFileTestExecutor.cs (main logic)
4. (30 min) Read MainConsoleMenu.cs (UI)
5. (30 min) First task: small improvement from roadmap

---

## 🔗 Important Links

- **Repository:** https://github.com/afrowaveltd/DiskChecker
- **AJIS Library:** https://github.com/afrowaveltd/Ajis.Dotnet
- **Roadmap:** `docs/DEVELOPMENT_ROADMAP.md`
- **Extended Plan:** `docs/DEVELOPMENT_ROADMAP_EXTENDED.md`
- **Implementation Notes:** `docs/IMPLEMENTATION_COMPLETED.md`
- **Disk Detection Fix:** `DISK_DETECTION_FIX.md`
- **SMART Diagnostics:** `SMART_DIAGNOSTICS.md`

---

## 📝 Latest Session Notes

### Current Status (2025-02-26)
- **Phase 1:** ✅ COMPLETE
  - Console UI working
  - SMART diagnostics operational
  - FullDiskSanitization test functional
  - Delta method + user fallback implemented
  - Comprehensive metadata collection added

- **Phase 1.5 (NEW):** 📋 PLANNED
  - AJIS per-disk storage design finalized
  - SQLite index schema defined
  - Need to migrate from EF Core to AJIS storage
  - Estimated: 2-3 weeks

- **Next Focus:** Phase 2 (Linux) or Phase 1.5 (AJIS Storage)
  - Recommend: Complete Phase 1.5 first (foundational)
  - Then Phase 2 (Linux) and Phase 6 (Localization) in parallel

### Key Decisions Made
1. **AJIS per-disk format** (locked for Phase 1-5)
2. **SQLite routing index** (DiskIndex.sqlite3)
3. **Delta method for drive detection** (100% reliable)
4. **User fallback on DriveInfo** (safe, explicit)
5. **Streaming R/W pattern** (memory efficient)

### Blockers / Known Issues
- None critical (Phase 1 complete!)
- smartctl path detection could be improved (non-blocking)
- Linux support still pending

### Files Changed (Last Session)
- `DiskChecker.Core/Models/SurfaceTestModels.cs` - added drive metadata
- `DiskChecker.Infrastructure/Hardware/SequentialFileTestExecutor.cs` - drive detection + metadata
- `DiskChecker.UI/Console/MainConsoleMenu.cs` - formatting improvements
- `docs/DEVELOPMENT_ROADMAP_EXTENDED.md` - enterprise roadmap
- `docs/AI_CONTEXT.md` - this file (created)

---

**Happy coding! 🚀**

*Last Updated: 2025-02-26 by Initial Session*

*Next AI Assistant: Please update this section when you finish your session!*
