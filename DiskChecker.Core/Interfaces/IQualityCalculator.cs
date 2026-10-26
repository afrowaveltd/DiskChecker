using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface IQualityCalculator
    {
        /// <summary>
        /// Calculates the quality rating for the given SMART data.
        /// Returns <c>null</c> when no SMART data is available at all (missing data,
        /// or a device that explicitly reports <c>smart_support.available == false</c>).
        /// A <c>null</c> rating means "not gradeable" — callers must show N/A and must
        /// never invent a grade (the disk simply exposes no SMART information).
        /// </summary>
        QualityRating? CalculateQuality(SmartaData? smartaData);
    }
}