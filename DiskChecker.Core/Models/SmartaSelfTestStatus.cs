namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Status of a SMART self-test.
    /// </summary>
    public enum SmartaSelfTestStatus
    {
        /// <summary>Status unknown or could not be determined</summary>
        Unknown = 0,
        
        /// <summary>No self-test has been run</summary>
        NoTest = 1,
        
        /// <summary>Self-test was aborted by user command</summary>
        AbortedByUser = 2,
        
        /// <summary>Self-test was aborted by host system</summary>
        AbortedByHost = 3,
        
        /// <summary>Fatal error during self-test</summary>
        FatalError = 4,
        
        /// <summary>Unknown error during self-test</summary>
        ErrorUnknown = 5,
        
        /// <summary>Electrical error during self-test</summary>
        ErrorElectrical = 6,
        
        /// <summary>Servo (mechanical) error during self-test</summary>
        ErrorServo = 7,
        
        /// <summary>Read error during self-test</summary>
        ErrorRead = 8,
        
        /// <summary>Handling error during self-test</summary>
        ErrorHandling = 9,
        
        /// <summary>Self-test completed without any errors</summary>
        CompletedWithoutError = 10,
        
        /// <summary>Self-test is currently in progress</summary>
        InProgress = 15
    }
}