using System.Buffers;
using System.Net.Sockets;
using System.Text;

namespace SillyRedis
{
    public class Server
    {
        private readonly TcpListener _listener;
        private readonly Func<string[], string> _response;

        public Server(TcpListener listener, Func<string[], string> response)
        {
            _listener = listener;
            _response = response;
        }

        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var acceptClient = await _listener.AcceptTcpClientAsync(token);

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
                    // Read request and break if client disconnected
                    var bytesRead = await stream.ReadAsync(buffer, token);
                    if (bytesRead == 0) break;

                    var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    var args = RESProtocol.ParseResp(request);
                    Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Received: {string.Join(' ', args)}");

                    //respond to client
                    var message = _response(args);
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes, token);
                    await stream.FlushAsync(token);
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
                    buffer = null;
                }
            }
        }
    }
}
