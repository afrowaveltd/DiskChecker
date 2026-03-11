using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using DiskChecker.Infrastructure.Configuration;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiskChecker.Tests
{
    public class WindowsSmartaProviderCacheTests
    {
        [Fact]
        public async Task CacheManagement_AddAndClear_Works()
        {
            var options = Options.Create(new SmartaCacheOptions { TtlMinutes = 10 });
            var provider = new WindowsSmartaProvider(new NullLogger<WindowsSmartaProvider>(), options);

            // Use reflection to inject a fake cache item
            var cacheField = typeof(WindowsSmartaProvider).GetField("_smartCache", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(cacheField);

            var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
            var key = "test-device";
            var data = new SmartaData { DeviceModel = "T", SerialNumber = "SN1", RetrievedAtUtc = DateTime.UtcNow };
            cache[key] = (data, DateTime.UtcNow);

            var statsBefore = await provider.GetSmartCacheStatsAsync();
            Assert.Equal(1, statsBefore.Items);

            await provider.RemoveSmartCacheForDeviceAsync(key);
            var statsAfterRemove = await provider.GetSmartCacheStatsAsync();
            Assert.Equal(0, statsAfterRemove.Items);

            // Add again and clear all
            cache[key] = (data, DateTime.UtcNow);
            await provider.ClearSmartCacheAsync();
            var statsAfterClear = await provider.GetSmartCacheStatsAsync();
            Assert.Equal(0, statsAfterClear.Items);
        }

        [Fact]
        public async Task SetCacheTtlMinutes_UpdatesValue()
        {
            var options = Options.Create(new SmartaCacheOptions { TtlMinutes = 5 });
            var provider = new WindowsSmartaProvider(new NullLogger<WindowsSmartaProvider>(), options);

            // ensure no exception
            await provider.SetCacheTtlMinutesAsync(20);
            await provider.SetCacheTtlMinutesAsync(1);
        }
    }
}
