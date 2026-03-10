// DiskChecker.Api/Hubs/DiskTestHub.cs
using Microsoft.AspNetCore.SignalR;

namespace DiskChecker.Api.Hubs;

/// <summary>
/// SignalR hub for disk test operations - STATELESS design.
/// This hub does NOT use Blazor Server circuit, so UAC is not an issue.
/// </summary>
public partial class DiskTestHub : Hub
{
    private readonly DiskCheckerService _diskService;
    private readonly ILogger<DiskTestHub> _logger;

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ConnectionId} connected")]
    private partial void LogClientConnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting disk test {RequestId} for client {ConnectionId}")]
    private partial void LogTestStarted(string requestId, string connectionId);

    public DiskTestHub(DiskCheckerService diskService, ILogger<DiskTestHub> logger)
    {
        _diskService = diskService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        LogClientConnected(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Request/Response pattern - client sends request, hub sends response.
    /// NO state is kept in memory - everything goes through DB.
    /// </summary>
    [HubMethod]
    public async Task<TestResponse> StartDiskTest(TestRequest request)
    {
        LogTestStarted(request.RequestId, Context.ConnectionId);

        try
        {
            // Process test (this CAN run as admin without UAC blocking)
            var result = await _diskService.RunTestAsync(request);

            // Response with matching requestId
            return new TestResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            return new TestResponse
            {
                RequestId = request.RequestId,
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Real-time progress updates - this is where SignalR shines!
    /// </summary>
    [HubMethod]
    public async Task SubscribeToTestProgress(string testId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"test-{testId}");
    }

    /// <summary>
    /// Server-initiated push notification (called from background service).
    /// </summary>
    public async Task BroadcastProgress(string testId, TestProgress progress)
    {
        await Clients.Group($"test-{testId}").SendAsync("ProgressUpdate", progress);
    }
}

// Request/Response models
public record TestRequest(
    string RequestId,
    string DiskPath,
    string Profile
);

public record TestResponse(
    string RequestId,
    bool Success,
    object? Result = null,
    string? Error = null
);

public record TestProgress(
    string TestId,
    double PercentComplete,
    double CurrentSpeed
);
