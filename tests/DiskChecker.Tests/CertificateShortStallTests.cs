using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using Xunit;

namespace DiskChecker.Tests;

public class CertificateShortStallTests
{
    [Fact]
    public void PdfSpeedProfile_PreservesASingleZeroSpeedStallAmongFastSamples()
    {
        var samples = new List<SpeedSample>();
        for (var i = 0; i < 40_000; i++)
        {
            samples.Add(new SpeedSample
            {
                ProgressPercent = i * 100d / 39_999,
                SpeedMBps = i == 10_001 ? 0 : 340,
                IsStalled = i == 10_001
            });
        }

        var reducer = typeof(CertificateGenerator).GetMethod(
            "DownsampleSpeedSamples", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(reducer);
        var profile = (List<double>)reducer!.Invoke(null, new object?[] { samples, 512 })!;
        Assert.InRange(profile.Count, 2, 512);
        Assert.Contains(0d, profile);
        Assert.Contains(340d, profile);
    }

    [Fact]
    public void ExistingCertificateProfile_DoesNotDiscardZeroSpeeds()
    {
        var certificate = new DiskCertificate
        {
            WriteProfilePoints = new List<double> { 340, 330, 0, 27, 30 },
            ReadProfilePoints = new List<double> { 430, 425, 0, 420 }
        };

        var getProfiles = typeof(CertificateGenerator).GetMethod(
            "GetProfilePointsForChart", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(getProfiles);
        var (write, read) = ((List<double> Write, List<double> Read))
            getProfiles!.Invoke(null, new object[] { certificate })!;

        Assert.Equal(5, write.Count);
        Assert.Equal(4, read.Count);
        Assert.Equal(0d, write[2]);
        Assert.Equal(0d, read[2]);
    }
}
