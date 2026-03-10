namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Represents a single entry in the SMART self-test log.
    /// </summary>
    public class SmartaSelfTestEntry
    {
        /// <summary>Test number in the log (most recent = 1)</summary>
        public int Number { get; set; }
        
        /// <summary>Type of self-test performed</summary>
        public SmartaSelfTestType Type { get; set; }
        
        /// <summary>Test type for XAML binding (alias for Type)</summary>
        public SmartaSelfTestType TestType { get => Type; set => Type = value; }
        
        /// <summary>Status/result of the self-test</summary>
        public SmartaSelfTestStatus Status { get; set; }
        
        /// <summary>Human-readable test type name</summary>
        public string TestTypeName => Type switch
        {
            SmartaSelfTestType.ShortTest => "Krátký",
            SmartaSelfTestType.Quick => "Rychlý",
            SmartaSelfTestType.Extended => "Rozšířený",
            SmartaSelfTestType.LongTest => "Dlouhý",
            SmartaSelfTestType.Conveyance => "Přepravní",
            SmartaSelfTestType.Selective => "Selektivní",
            SmartaSelfTestType.Offline => "Offline",
            SmartaSelfTestType.Abort => "Přerušen",
            SmartaSelfTestType.Captive => "Captive",
            _ => "Neznámý"
        };
        
        /// <summary>Human-readable status name</summary>
        public string StatusName => Status switch
        {
            SmartaSelfTestStatus.CompletedWithoutError => "✅ Úspěšný",
            SmartaSelfTestStatus.InProgress => "⏳ Probíhá",
            SmartaSelfTestStatus.AbortedByUser => "⏹️ Přerušen uživatelem",
            SmartaSelfTestStatus.AbortedByHost => "⏹️ Přerušen systémem",
            SmartaSelfTestStatus.FatalError => "❌ Fatální chyba",
            SmartaSelfTestStatus.ErrorUnknown => "❌ Neznámá chyba",
            SmartaSelfTestStatus.ErrorElectrical => "⚡ Elektrická chyba",
            SmartaSelfTestStatus.ErrorServo => "🔧 Servo chyba",
            SmartaSelfTestStatus.ErrorRead => "📖 Chyba čtení",
            SmartaSelfTestStatus.ErrorHandling => "⚠️ Chyba obsluhy",
            _ => "❓ Neznámý"
        };
        
        /// <summary>Progress percentage if test is running (0-100)</summary>
        public int? RemainingPercent { get; set; }
        
        /// <summary>Power-on hours when test was completed</summary>
        public int? LifeTimeHours { get; set; }
        
        /// <summary>LBA of first error if any</summary>
        public long? LbaOfFirstError { get; set; }
        
        /// <summary>Timestamp when test completed</summary>
        public string? CompletedAt { get; set; }
        
        /// <summary>Formatted lifetime hours for display</summary>
        public string FormattedHours => LifeTimeHours.HasValue ? $"{LifeTimeHours.Value:N0} h" : "-";
        
        /// <summary>Formatted progress for running tests</summary>
        public string FormattedProgress => RemainingPercent.HasValue ? $"{100 - RemainingPercent.Value}%" : "-";
    }
}