using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

public class DiskCheckerService
{
   private readonly ISmartaProvider _smartaProvider;
   private readonly IQualityCalculator _qualityCalculator;

   public DiskCheckerService(ISmartaProvider smartaProvider, IQualityCalculator qualityCalculator)
   {
      _smartaProvider = smartaProvider;
      _qualityCalculator = qualityCalculator;
   }

   public async Task<SmartaData?> GetDiskInfoAsync(string drivePath)
   {
      var smartaData = await _smartaProvider.GetSmartaDataAsync(drivePath);
      return smartaData;
   }

   public async Task<QualityRating> CalculateQualityAsync(string drivePath)
   {
      var smartaData = await _smartaProvider.GetSmartaDataAsync(drivePath);
      if(smartaData == null)
      {
         return new(QualityGrade.F, 0.0);
      }

      return _qualityCalculator.CalculateQuality(smartaData);
   }

   public async Task<bool> IsDriveValidAsync(string drivePath)
   {
      return await _smartaProvider.IsDriveValidAsync(drivePath);
   }

   public async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync()
   {
      var drives = await _smartaProvider.ListDrivesAsync();
      // Temporary placeholder - convert List<string> to CoreDriveInfo if needed
      return new List<CoreDriveInfo>();
   }
}
