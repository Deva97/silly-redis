using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;

var ipAddress = IPAddress.Parse("127.0.0.1");
var server = new TcpListener(ipAddress, 6379);
server.Start();
var source = new CancellationTokenSource();
var token = source.Token;

var failedTask = new List<string>();

try
{
    while (!token.IsCancellationRequested)
    {
        using var acceptClinet = await server.AcceptTcpClientAsync(token);

        _ = HandleClientAsync(acceptClinet, token);

    }
    await using var stream =  acceptClinet.GetStream();
    var message = "+PONG\r\n";
    var messageBytes = Encoding.UTF8.GetBytes(message);
   
    var buffer = new byte[1024];
    while(true)
    {
        await stream.WriteAsync(messageBytes, cancellationToken: token);
    }
}
catch(OperationCanceledException e)
{
    
}
finally
{
    server.Stop();
}

async Task HandleClientAsync(TcpClient client, CancellationToken token)
{
    try
    {
        Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Connected");
        await using var stream = client.GetStream();
        var message = "+PONG\r\n";
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var buffer = new byte[1024];
        while (!token.IsCancellationRequested)
        {
            var bytesRead = await stream.ReadAsync(buffer, token);
            if (bytesRead == 0) break;

            var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Received: {request}");
            await stream.WriteAsync(messageBytes, token);
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
    }
}