using DiskChecker.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace DiskChecker.Web.Hubs;

/// <summary>
/// SignalR hub for real-time disk test progress updates.
/// Enables push notifications instead of polling.
/// </summary>
public class DiskTestHub : Hub
{
    private readonly ILogger<DiskTestHub> _logger;

    public DiskTestHub(ILogger<DiskTestHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {connectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {connectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client joins a test room for receiving updates.
    /// </summary>
    public async Task JoinTestRoom(string testId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"test-{testId}");
        _logger.LogInformation("Client {connectionId} joined test {testId}", Context.ConnectionId, testId);
    }

    /// <summary>
    /// Client leaves the test room.
    /// </summary>
    public async Task LeaveTestRoom(string testId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"test-{testId}");
        _logger.LogInformation("Client {connectionId} left test {testId}", Context.ConnectionId, testId);
    }

    /// <summary>
    /// Server notifies clients of progress update.
    /// Called by the test service when progress is reported.
    /// </summary>
    public async Task NotifyProgressUpdate(string testId, SurfaceTestProgress progress)
    {
        await Clients.Group($"test-{testId}").SendAsync("ProgressUpdate", progress);
    }

    /// <summary>
    /// Server notifies clients when test completes.
    /// </summary>
    public async Task NotifyTestComplete(string testId, SurfaceTestResult result)
    {
        await Clients.Group($"test-{testId}").SendAsync("TestComplete", result);
    }

    /// <summary>
    /// Server notifies clients of an error.
    /// </summary>
    public async Task NotifyError(string testId, string errorMessage)
    {
        await Clients.Group($"test-{testId}").SendAsync("TestError", errorMessage);
    }
}

/// <summary>
/// Service for broadcasting test updates via SignalR.
/// </summary>
public class TestProgressBroadcaster
{
    private readonly IHubContext<DiskTestHub> _hubContext;
    private readonly ILogger<TestProgressBroadcaster> _logger;

    public TestProgressBroadcaster(IHubContext<DiskTestHub> hubContext, ILogger<TestProgressBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastProgressAsync(string testId, SurfaceTestProgress progress)
    {
        try
        {
            await _hubContext.Clients.Group($"test-{testId}").SendAsync("ProgressUpdate", progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting progress for test {testId}", testId);
        }
    }

    public async Task BroadcastCompleteAsync(string testId, SurfaceTestResult result)
    {
        try
        {
            await _hubContext.Clients.Group($"test-{testId}").SendAsync("TestComplete", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting completion for test {testId}", testId);
        }
    }

    public async Task BroadcastErrorAsync(string testId, string errorMessage)
    {
        try
        {
            await _hubContext.Clients.Group($"test-{testId}").SendAsync("TestError", errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting error for test {testId}", testId);
        }
    }
}
