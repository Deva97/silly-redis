using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace SillyRedis
{
    public class Server(TcpListener listener, Func<string[], string> response)
    {
        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var acceptClient = await listener.AcceptTcpClientAsync(token);

                //fire and forget. not using await her since waiting make the system to
                // to behave in such a way that it can handle only one client at a time.
                // we want to handle multiple clients concurrently.
                _ = HandleClientAsync(acceptClient, token);
            }
        }

        async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            byte[]? buffer = null;
            try
            {
                Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Connected");
                await using var stream = client.GetStream();
                buffer = ArrayPool<byte>.Shared.Rent(1024);

                while (!token.IsCancellationRequested)
                {
                    var args = await ReadRequestAsync(stream, buffer, token);
                    if (args == null) break;

                    Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Received: {string.Join(' ', args)}");

                    var message = response(args);
                    await WriteResponseAsync(stream, message, token);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] ERROR: {e.Message}");
            }
            finally
            {
                Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Disconnected");
                client.Close();
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        // Returns parsed args, or null if the client disconnected.
        static async Task<string[]?> ReadRequestAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
        {
            using var accumulator = new MemoryStream();

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, token);
                if (bytesRead == 0) return null;

                accumulator.Write(buffer.AsSpan(0, bytesRead));

                ReadOnlySpan<byte> accumulated = accumulator.GetBuffer().AsSpan(0, (int)accumulator.Length);
                if (IsCompleteRespMessage(accumulated))
                {
                    var request = Encoding.UTF8.GetString(accumulated).Trim();
                    return RESProtocol.ParseResp(request);
                }
            }
        }

        // Each *N contributes 1 \r\n; each $M (bulk string) contributes 2 (\r\n for header + data)
        static bool IsCompleteRespMessage(ReadOnlySpan<byte> data)
        {
            var text = Encoding.UTF8.GetString(data);
            var lines = text.Split("\r\n");

            int starCount   = lines.Count(l => l.StartsWith('*'));
            int dollarCount = lines.Count(l => l.StartsWith('$'));

            if (starCount == 0) return false;

            int expectedCrLf = starCount + dollarCount * 2;
            int actualCrLf   = lines.Length - 1;

            return actualCrLf >= expectedCrLf;
        }

        static async Task WriteResponseAsync(NetworkStream stream, string message, CancellationToken token)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(messageBytes, token);
            await stream.FlushAsync(token);
        }
    }
}
