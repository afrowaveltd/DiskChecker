using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Core.Models;
using System.Linq;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Adapter that exposes the application HistoryService through the UI-facing IHistoryService interface.
/// This avoids adding a project dependency from the application project to the UI project.
/// </summary>
internal class HistoryServiceAdapter : IHistoryService
{
    private readonly DiskChecker.Application.Services.HistoryService _inner;

    public HistoryServiceAdapter(DiskChecker.Application.Services.HistoryService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<IEnumerable<HistoricalTest>> GetHistoryAsync()
    {
        var results = await _inner.GetHistoryAsync(CancellationToken.None);
        return results.Select(r => r); // passthrough - types align via DiskChecker.Core.Models
    }

    public async Task<IEnumerable<HistoricalTest>> GetHistoryForDiskAsync(string serialNumber)
    {
        var results = await _inner.GetHistoryForDiskAsync(serialNumber, CancellationToken.None);
        return results.Select(r => r);
    }

    public Task DeleteHistoryAsync(Guid testId)
        => _inner.DeleteHistoryAsync(testId, CancellationToken.None);

    public Task ClearHistoryAsync()
        => _inner.ClearHistoryAsync(CancellationToken.None);
}
