// =============================================================================
// ORGANIZATION NOTES - DiskChecker.Infrastructure
// =============================================================================
// 
// This file contains notes about the organization of the Hardware folder.
// Large files that could benefit from splitting:
// 
// 1. RawDiskSanitizationExecutor.cs (~1500 lines)
//    - Could be split into:
//      - Win32DiskApi.cs (P/Invoke declarations)
//      - RawDiskSanitizationExecutor.cs (main logic)
//
// 2. SurfaceTestExecutor.cs (~789 lines)
//    - Could be split into:
//      - SurfaceTestConstants.cs (constants and patterns)
//      - SurfaceTestExecutor.cs (main logic)
//
// 3. DiskSurfaceTestExecutor.cs (~565 lines)
//    - Could be split into:
//      - SurfaceTestModels.cs (data models)
//      - DiskSurfaceTestExecutor.cs (main logic)
//
// Current organization is functional but could be improved for maintainability.
// =============================================================================
