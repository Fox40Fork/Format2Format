using System.IO.Pipes;
using System.Text;

namespace Format2Format.Core;

/// <summary>
/// Keeps one app window per user session. The first process owns a named mutex and listens on a named pipe;
/// later processes (e.g. Explorer starting one process per selected file) send their file paths through the
/// pipe and exit.
/// </summary>
/// <remarks>
/// Protocol: the client connects, writes one path per line (UTF-8), and closes the connection.
/// Both ends use <see cref="PipeOptions.CurrentUserOnly"/>, so other users can't connect.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    /// <param name="instanceId">
    /// Unique per app and user/session, e.g. "Format2Format-S-1-5-21-...-1". Used for the mutex and pipe names.
    /// </param>
    public SingleInstance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        _pipeName = instanceId;
        _mutex = new Mutex(initiallyOwned: false, name: @"Local\" + instanceId);
    }

    /// <summary>True once this process owns the mutex (it is the primary instance).</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// Raised on a thread-pool thread whenever another process forwards its arguments.
    /// An empty array means "just bring the window to the front".
    /// </summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>Tries to become the primary instance, waiting up to <paramref name="timeoutMs"/>.</summary>
    public bool TryBecomePrimary(int timeoutMs = 0)
    {
        if (IsPrimary)
            return true;

        try
        {
            IsPrimary = _mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true; // the previous owner crashed; the mutex is ours now
        }

        return IsPrimary;
    }

    /// <summary>Sends arguments to the primary instance. Returns false if it couldn't be reached in time.</summary>
    public bool TrySendToPrimary(IReadOnlyList<string> args, int timeoutMs = 5000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);

            using (var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true))
            {
                foreach (string arg in args)
                    writer.WriteLine(arg);
            }

            if (OperatingSystem.IsWindows())
                client.WaitForPipeDrain();

            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not hand arguments to the running instance: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts listening for other processes. Only valid for the primary instance.</summary>
    public void StartServer()
    {
        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can listen for other instances.");
        if (_serverTask != null)
            return;

        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_serverCts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex)
            {
                Log.Error("Could not create the single-instance pipe", ex);
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("Single-instance pipe connection failed", ex);
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            // Handle the client in the background and immediately accept the next one, so a burst of
            // processes (one per selected file) is served quickly.
            _ = Task.Run(() => HandleClientAsync(server, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var args = new List<string>();
        try
        {
            await using (server.ConfigureAwait(false))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(server, Encoding.UTF8);

                while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 0)
                        args.Add(line);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Error reading from another instance", ex);
            if (args.Count == 0)
                return;
        }

        try
        {
            ArgumentsReceived?.Invoke(args.ToArray());
        }
        catch (Exception ex)
        {
            Log.Error("ArgumentsReceived handler failed", ex);
        }
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // already logged / shutting down
        }

        _serverCts?.Dispose();

        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Released from a different thread than it was acquired on; the OS frees it at exit anyway.
            }

            IsPrimary = false;
        }

        _mutex.Dispose();
    }
}
