using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Zing;

/// <summary>
/// One window for files opened from Explorer (ADR-001). The first Zing of a user's session owns a
/// named mutex and listens on a named pipe; a later launch hands its file over the pipe and exits.
/// Both ends use <see cref="PipeOptions.CurrentUserOnly"/>: the pipe accepts only the same Windows
/// user, and the client refuses a pipe created by anyone else.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024;
    private const byte Ack = 1;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private bool _disposed;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Per user and per logon session: pipe names are machine-wide, so both are in the name.</summary>
    public static string DefaultChannel =>
        $"Zing-{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}-{Process.GetCurrentProcess().SessionId}";

    /// <summary>Becomes the primary instance, or returns null if another Zing already is.</summary>
    public static SingleInstance? TryBecomePrimary(string channel)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + channel, out bool created);
        if (created) return new SingleInstance(mutex, channel);
        mutex.Dispose();
        return null;
    }

    // ---------- message: 4-byte little-endian length, then UTF-8 "open\n<path>" or "activate" ----------

    public static byte[] Encode(string? path)
    {
        byte[] body = Encoding.UTF8.GetBytes(path is null ? "activate" : "open\n" + path);
        var message = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message, body.Length);
        body.CopyTo(message, 4);
        return message;
    }

    /// <summary>
    /// Decodes a message body. A path must be fully qualified and free of invalid characters,
    /// exactly what a command-line launch would pass; anything else is rejected.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> body, out string? path)
    {
        path = null;
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(body); }
        catch (ArgumentException) { return false; }

        if (text == "activate") return true;
        if (!text.StartsWith("open\n", StringComparison.Ordinal)) return false;
        string candidate = text["open\n".Length..];
        if (candidate.Length is 0 or > 32_767 || candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            candidate.Contains('\n') || !Path.IsPathFullyQualified(candidate))
            return false;
        path = candidate;
        return true;
    }

    // ---------- client: the later launch ----------

    /// <summary>
    /// Hands <paramref name="path"/> (null: just bring the window forward) to the primary instance.
    /// True only when the primary confirmed it; false within <paramref name="budget"/> otherwise,
    /// so the caller opens its own window instead of losing the file.
    /// </summary>
    public static bool TrySend(string channel, string? path, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        byte[] message = Encode(path);
        AllowSetForegroundWindow(AsfwAny); // the primary may come to the front: this launch has the right to

        while (clock.Elapsed < budget)
        {
            int left = (int)(budget - clock.Elapsed).TotalMilliseconds;
            if (left <= 0) break;
            try
            {
                using var client = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                client.Connect(Math.Min(left, 500));
                using var timeout = new CancellationTokenSource(Math.Max(1, (int)(budget - clock.Elapsed).TotalMilliseconds));
                client.WriteAsync(message, timeout.Token).AsTask().GetAwaiter().GetResult();
                var reply = new byte[1];
                int read = client.ReadAsync(reply, timeout.Token).AsTask().GetAwaiter().GetResult();
                return read == 1 && reply[0] == Ack;
            }
            catch (TimeoutException) { /* primary not listening yet: retry until the budget runs out */ }
            catch (IOException) { Thread.Sleep(50); }
            catch (OperationCanceledException) { return false; }
            catch (UnauthorizedAccessException) { return false; } // pipe owned by another user
        }
        return false;
    }

    // ---------- server: the primary instance ----------

    /// <summary>
    /// Serves hand-offs on a background task until disposed. <paramref name="deliver"/> runs on that
    /// task with the decoded path (null = activate) and returns true once the request is queued for
    /// the window; only then is the sender told it can exit.
    /// </summary>
    public void Listen(Func<string?, bool> deliver)
    {
        CancellationToken stop = _stop.Token;
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    timeout.CancelAfter(2000); // a client that connects and says nothing does not block others for long
                    byte[]? body = await ReadBodyAsync(server, timeout.Token);
                    if (body is not null && TryDecode(body, out string? path) && deliver(path))
                        await server.WriteAsync(new[] { Ack }, timeout.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    // a broken client, or the pipe name is taken: keep serving, without spinning
                    try { await Task.Delay(200, stop); } catch (OperationCanceledException) { break; }
                }
                finally { server?.Dispose(); }
            }
        });
    }

    private static async Task<byte[]?> ReadBodyAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes) return null;
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return body;
    }

    /// <summary>Must run on the thread that created the instance: a mutex belongs to its owning thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose(); // _stop is left to the GC: the listener may still be observing its token
    }

    private const int AsfwAny = -1;
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
