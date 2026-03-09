
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Extensions
{
    public static class DoubleExtensions
    {
        public static string GenerateCertificate(this double value)
        {
            return value.ToString();
        }

        public static QualityGrade Grade(this double value)
        {
            if (value >= 90) return QualityGrade.A;
            if (value >= 80) return QualityGrade.B;
            if (value >= 70) return QualityGrade.C;
            if (value >= 60) return QualityGrade.D;
            return QualityGrade.F;
        }

        public static double Score(this double value)
        {
            return value;
        }
    }
}
