using System.Net;
using System.Net.Sockets;
using System.Text;

var ipAddress = IPAddress.Parse("127.0.0.1");
var server = new TcpListener(ipAddress, 6379);
server.Start();

using var source = new CancellationTokenSource();
var token = source.Token;
var clientTasks = new List<Task>();

try
{
    while (!token.IsCancellationRequested)
    {
        var acceptedClient = await server.AcceptTcpClientAsync(token);
        clientTasks.Add(HandleClientAsync(acceptedClient, token));
    }
}
catch (OperationCanceledException)
{
    // Expected when cancellation is requested.
}
finally
{
    server.Stop();

    if (clientTasks.Count > 0)
    {
        await Task.WhenAll(clientTasks);
    }
}

async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
{
    try
    {
        Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Connected");
        await using var stream = client.GetStream();
        var pongResponseBytes = Encoding.UTF8.GetBytes("+PONG\r\n");
        var buffer = new byte[1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Received: {request}");

            await stream.WriteAsync(pongResponseBytes, cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
        // Ignore cancellation exceptions during shutdown.
    }
    catch (Exception exception)
    {
        Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] ERROR: {exception.Message}");
    }
    finally
    {
        Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Disconnected");
        client.Close();
    }
}
