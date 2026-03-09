namespace DiskChecker.Core.Models {
      public class QualityRating {
          public QualityGrade Grade { get; set; } = QualityGrade.C;
          public double Score { get; set; }
          public List<string> Warnings { get; set; } = new();
          public QualityRating() { }
          public QualityRating(QualityGrade grade, double score) {
              Grade = grade;
              Score = score;
          }
      }
    }