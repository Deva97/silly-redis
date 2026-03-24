using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("=== TCP Server Concurrent Clients Test ===\n");

var ipAddress = IPAddress.Parse("127.0.0.1");
const int numClients = 10;
const int requestsPerClient = 5;

var successCount = 0;
var errorCount = 0;
var latencies = new List<double>();

async Task ClientWorker(int clientId)
{
    try
    {
        using var client = new TcpClient();
        await client.ConnectAsync(ipAddress, 6379);
        Console.WriteLine($"[Client {clientId:D2}] Connected");

        await using var stream = client.GetStream();

        for (int req = 1; req <= requestsPerClient; req++)
        {
            var message = $"PING {clientId}:{req}\r\n";
            var messageBytes = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(messageBytes);

            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);

            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            lock (latencies)
            {
                successCount++;
            }
            Console.WriteLine($"[Client {clientId:D2}] Request {req}: Received '{response}'");
    }
    }
    catch (Exception e)
    {
        Console.WriteLine($"[Client {clientId:D2}] ERROR: {e.Message}");
        lock (latencies)
        {
            errorCount++;
        }
    }
}

try
{
    Console.WriteLine($"Starting {numClients} concurrent clients with {requestsPerClient} requests each\n");

    var tasks = new List<Task>();
    for (int i = 1; i <= numClients; i++)
    {
        tasks.Add(ClientWorker(i));
    }

    await Task.WhenAll(tasks);

    Console.WriteLine($"\n=== Test Results ===");
    Console.WriteLine($"Successful requests: {successCount}");
    Console.WriteLine($"Failed requests: {errorCount}");
}
catch (Exception e)
{
    Console.WriteLine($"Test Error: {e}");
}

