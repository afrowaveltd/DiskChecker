using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Fetches a bounded full-range sample from every seek mode. All certificate
/// screens share this loader and the plot projection; the PDF export remains a
/// distinct, one-page rendering contract.
/// </summary>
public static class CertificateSeekChartLoader
{
    public static async Task<CertificateSeekGraphData> LoadAsync(
        IDiskCardRepository repository, int sessionId, string? legacyJson,
        IReadOnlyList<double>? historical)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if(sessionId > 0)
        {
            try
            {
                var all = new List<SeekSampleRecord>(3 * CertificateSeekChartProjector.MaxSamplesPerMode);
                foreach (var phase in new[]
                {
                    SeekTestType.FullStroke,
                    SeekTestType.Random,
                    SeekTestType.Skip
                })
                {
                    all.AddRange(await repository.GetSeekSamplesDownsampledAsync(
                        sessionId, CertificateSeekChartProjector.MaxSamplesPerMode, phase));
                }

                if(all.Count > 0)
                    return CertificateSeekChartProjector.Build(all);
            }
            catch (Exception ex) when (ex is DbException or IOException or InvalidOperationException)
            {
                // Older imported databases may lack SeekSamples. The small JSON
                // history below is only a fallback and is bounded by byte count.
            }
        }

        return CertificateSeekChartProjector.FromLegacy(legacyJson, historical);
    }
}
