namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Represents a single SMART attribute from a drive.
    /// </summary>
    public class SmartaAttributeItem
    {
        /// <summary>Attribute ID (e.g., 5 for Reallocated Sectors)</summary>
        public int Id { get; set; }
        
        /// <summary>Attribute name (e.g., "Reallocated_Sector_Ct")</summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>Normalized value (0-100, higher is better)</summary>
        public int Value { get; set; }
        
        /// <summary>Raw value from attribute</summary>
        public byte Current { get; set; }
        
        /// <summary>Worst recorded value (normalized, 0-100)</summary>
        public int Worst { get; set; }
        
        /// <summary>Threshold value (below this is failure)</summary>
        public int Threshold { get; set; }
        
        /// <summary>Raw uninterpreted value</summary>
        public uint RawValue { get; set; }
        
        /// <summary>True if attribute is OK (not failing)</summary>
        public bool IsOk { get; set; }
        
        /// <summary>When this attribute failed (if applicable)</summary>
        public string WhenFailed { get; set; } = string.Empty;
        
        /// <summary>Human-readable interpretation of raw value</summary>
        public string Interpretation { get; set; } = string.Empty;
    }
}