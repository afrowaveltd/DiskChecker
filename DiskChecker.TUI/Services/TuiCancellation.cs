namespace DiskChecker.TUI.Services;

/// <summary>
/// Coordinates Ctrl+C between the shell and long running operations: the first
/// interrupt cancels the current operation, a second one leaves the application.
/// </summary>
public sealed class TuiCancellation : IDisposable
{
    private readonly CancellationTokenSource _app = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _operation;
    private bool _exitRequested;

    public CancellationToken AppToken => _app.Token;

    public bool HasActiveOperation
    {
        get
        {
            lock (_gate)
            {
                return _operation is not null;
            }
        }
    }

    public CancellationTokenSource BeginOperation()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_app.Token);
        lock (_gate)
        {
            _operation?.Dispose();
            _operation = cts;
        }

        return cts;
    }

    public void EndOperation(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_operation, cts))
            {
                _operation = null;
            }
        }

        cts.Dispose();
    }

    /// <summary>Returns true when the interrupt should end the application.</summary>
    public bool HandleInterrupt()
    {
        lock (_gate)
        {
            if (_operation is { IsCancellationRequested: false } active)
            {
                active.Cancel();
                return false;
            }
        }

        _exitRequested = true;
        _app.Cancel();
        return true;
    }

    public bool ExitRequested => _exitRequested;

    public void Dispose()
    {
        lock (_gate)
        {
            _operation?.Dispose();
            _operation = null;
        }

        _app.Dispose();
    }
}
