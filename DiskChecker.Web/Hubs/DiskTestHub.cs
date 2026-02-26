using DiskChecker.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace DiskChecker.Web.Hubs;

/// <summary>
/// SignalR hub for real-time disk test progress updates.
/// Enables push notifications instead of polling.
/// </summary>
public partial class DiskTestHub : Hub
{
    private readonly ILogger<DiskTestHub> _logger;

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected: {ConnectionId}")]
    private partial void LogClientConnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected: {ConnectionId}")]
    private partial void LogClientDisconnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ConnectionId} joined test {TestId}")]
    private partial void LogClientJoinedTest(string connectionId, string testId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ConnectionId} left test {TestId}")]
    private partial void LogClientLeftTest(string connectionId, string testId);

    public DiskTestHub(ILogger<DiskTestHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        LogClientConnected(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        LogClientDisconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client joins a test room for receiving updates.
    /// </summary>
    public async Task JoinTestRoom(string testId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"test-{testId}");
        LogClientJoinedTest(Context.ConnectionId, testId);
    }

    /// <summary>
    /// Client leaves the test room.
    /// </summary>
    public async Task LeaveTestRoom(string testId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"test-{testId}");
        LogClientLeftTest(Context.ConnectionId, testId);
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
public partial class TestProgressBroadcaster
{
    private readonly IHubContext<DiskTestHub> _hubContext;
    private readonly ILogger<TestProgressBroadcaster> _logger;

    [LoggerMessage(Level = LogLevel.Error, Message = "Error broadcasting progress for test {TestId}")]
    private partial void LogBroadcastProgressError(Exception ex, string testId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error broadcasting completion for test {TestId}")]
    private partial void LogBroadcastCompleteError(Exception ex, string testId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error broadcasting error for test {TestId}")]
    private partial void LogBroadcastErrorError(Exception ex, string testId);

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
            LogBroadcastProgressError(ex, testId);
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
            LogBroadcastCompleteError(ex, testId);
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
            LogBroadcastErrorError(ex, testId);
        }
    }
}
