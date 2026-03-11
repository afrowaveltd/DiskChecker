namespace DiskChecker.Infrastructure.Configuration
{
    /// <summary>
    /// Options for SMART provider cache behavior.
    /// </summary>
    public class SmartaCacheOptions
    {
        /// <summary>
        /// Cache TTL in minutes. Default 10.
        /// </summary>
        public int TtlMinutes { get; set; } = 10;
    }
}
