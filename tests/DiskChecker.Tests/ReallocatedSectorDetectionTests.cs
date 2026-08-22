using System;
using System.Collections.Generic;
using System.Reflection;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public class ReallocatedSectorDetectionTests
{
    [Fact]
    public void WindowsSmartJsonParser_PrefersId5_OverId196()
    {
        // Seagate-style attributes: ID 5 = real reallocated sectors, ID 196 = event count.
        var attributesJson = """
        [
            { "Id": 5, "Name": "Reallocated_Sector_Ct", "RawValue": 3 },
            { "Id": 196, "Name": "Reallocated_Event_Count", "RawValue": 120 }
        ]
        """;

        var result = WindowsSmartJsonParser.Parse("""{ "Model": "ST1000DM003" }""", attributesJson);

        Assert.NotNull(result);
        Assert.Equal(3, result.ReallocatedSectorCount);
    }

    [Fact]
    public void WindowsSmartJsonParser_StillMatchesId5_WhenOnlyId5Present()
    {
        var attributesJson = """
        [
            { "Id": 5, "Name": "Reallocated_Sector_Ct", "RawValue": 7 }
        ]
        """;

        var result = WindowsSmartJsonParser.Parse("""{ "Model": "ST1000DM003" }""", attributesJson);

        Assert.NotNull(result);
        Assert.Equal(7, result.ReallocatedSectorCount);
    }

    [Fact]
    public void FindRawCounter_PrefersExactId5_OverLargerId196()
    {
        var attrs = new List<SmartaAttributeItem>
        {
            new() { Id = 5, Name = "Reallocated_Sector_Ct", RawValue = 3 },
            new() { Id = 196, Name = "Reallocated_Event_Count", RawValue = 120 }
        };

        var result = InvokeFindRawCounter(attrs, 5, "Reallocated_Sector");

        Assert.Equal(3, result);
    }

    [Fact]
    public void FindRawCounter_FallsBackToName_WhenExactIdMissing()
    {
        var attrs = new List<SmartaAttributeItem>
        {
            new() { Id = 196, Name = "Reallocated_Event_Count", RawValue = 120 }
        };

        // No ID 5 present, and the name fragment "Reallocated_Sector" does not match
        // "Reallocated_Event_Count", so the result must be null (not 120).
        var result = InvokeFindRawCounter(attrs, 5, "Reallocated_Sector");

        Assert.Null(result);
    }

    private static int? InvokeFindRawCounter(IEnumerable<SmartaAttributeItem> attrs, int id, string nameFragment)
    {
        var type = typeof(DiskChecker.UI.Avalonia.ViewModels.SmartCheckViewModel);
        var method = type.GetMethod("FindRawCounter", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (int?)method!.Invoke(null, new object[] { attrs, id, nameFragment });
    }
}
