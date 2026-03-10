using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface IQualityCalculator
    {
        QualityRating CalculateQuality(SmartaData smartaData);
    }
}