using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SillyRedisMcp.RedisClient;

public interface IRedisClient
{
    Task<RespResult> SendAsync(string[] args, CancellationToken ct = default);
}

public sealed class RedisClient : IRedisClient, IHostedService, IAsyncDisposable
{
    private const string Host = "127.0.0.1";
    private const int Port = 6379;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<RedisClient> _logger;

    public RedisClient(ILogger<RedisClient> logger) => _logger = logger;

    public async Task StartAsync(CancellationToken ct)
    {
        await ConnectAsync(ct);
        _logger.LogInformation("Connected to silly-redis at {Host}:{Port}", Host, Port);
    }

    public Task StopAsync(CancellationToken ct) => DisposeAsync().AsTask();

    public async Task<RespResult> SendAsync(string[] args, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct);
            await WriteRespArrayAsync(_stream!, args, ct);
            return await ReadRespResponseAsync(_stream!, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Redis send failed, will reconnect on next call");
            await SafeDisconnectAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_tcp is { Connected: true })
            return;
        await SafeDisconnectAsync();
        await ConnectAsync(ct);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(Host, Port, ct);
        _stream = _tcp.GetStream();
    }

    private Task SafeDisconnectAsync()
    {
        try { _stream?.Dispose(); } catch { /* ignored */ }
        try { _tcp?.Dispose(); } catch { /* ignored */ }
        _stream = null;
        _tcp = null;
        return Task.CompletedTask;
    }

    private static async Task WriteRespArrayAsync(NetworkStream stream, string[] args, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append($"*{args.Length}\r\n");
        foreach (var arg in args)
        {
            var encoded = Encoding.UTF8.GetByteCount(arg);
            sb.Append($"${encoded}\r\n{arg}\r\n");
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<RespResult> ReadRespResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        using var acc = new MemoryStream();
        try
        {
            while (true)
            {
                int n = await stream.ReadAsync(buffer, ct);
                if (n == 0)
                    throw new EndOfStreamException("Redis connection closed unexpectedly.");

                acc.Write(buffer, 0, n);

                var data = acc.GetBuffer().AsSpan(0, (int)acc.Length);
                try
                {
                    return RespParser.Parse(data, out _);
                }
                catch (InsufficientDataException)
                {
                    // Need more bytes — keep reading
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await SafeDisconnectAsync();
        _lock.Dispose();
    }
}
