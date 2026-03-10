namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Types of SMART self-tests supported by drives.
    /// </summary>
    public enum SmartaSelfTestType
    {
        /// <summary>Unknown or unspecified test type</summary>
        Unknown = 0,
        
        /// <summary>Short self-test (usually 1-2 minutes)</summary>
        ShortTest = 1,
        
        /// <summary>Extended/Long self-test (full surface scan)</summary>
        Extended = 2,
        
        /// <summary>Conveyance self-test (for shipping verification)</summary>
        Conveyance = 3,
        
        /// <summary>Selective self-test (specific LBA range)</summary>
        Selective = 4,
        
        /// <summary>Offline immediate test (background)</summary>
        Offline = 5,
        
        /// <summary>Abort current self-test</summary>
        Abort = 6,
        
        /// <summary>Captive mode (blocks other commands during test)</summary>
        Captive = 7,
        
        /// <summary>No test</summary>
        NoTest = 8,
        
        /// <summary>Quick test (short test)</summary>
        Quick = 9,
        
        /// <summary>Long test (extended)</summary>
        LongTest = 10
    }
}