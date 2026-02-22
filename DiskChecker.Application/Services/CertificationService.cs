using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;

namespace DiskChecker.Application.Services;

public class CertificationService
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;

    public CertificationService(ISmartaProvider smartaProvider, IQualityCalculator qualityCalculator)
    {
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
    }

    public async Task<string> GenerateCertificateTextAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var smartaData = await _smartaProvider.GetSmartaDataAsync(drivePath, cancellationToken);
        if (smartaData == null)
        {
            return "Chyba: Nelze získat SMART data pro daný disk.";
        }

        var qualityRating = _qualityCalculator.CalculateQuality(smartaData);
        var testDate = DateTime.Now;

        return qualityRating.GenerateCertificate(smartaData, testDate);
    }

    public async Task SaveCertificateToFileAsync(string drivePath, string filePath, CancellationToken cancellationToken = default)
    {
        var certificateText = await GenerateCertificateTextAsync(drivePath, cancellationToken);
        File.WriteAllText(filePath, certificateText);
    }
}
