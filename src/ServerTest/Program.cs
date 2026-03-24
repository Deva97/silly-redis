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

            var stopwatch = Stopwatch.StartNew();
            await stream.WriteAsync(messageBytes);

            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer);
            stopwatch.Stop();

            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            lock (latencies)
            {
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                successCount++;
            }

            Console.WriteLine($"[Client {clientId:D2}] Req {req:D2} -> {response} ({stopwatch.Elapsed.TotalMilliseconds:F2}ms)");
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
    var testStopwatch = Stopwatch.StartNew();
    Console.WriteLine($"Starting {numClients} concurrent clients with {requestsPerClient} requests each\n");

    var tasks = new List<Task>();
    for (int i = 1; i <= numClients; i++)
    {
        tasks.Add(ClientWorker(i));
    }

    await Task.WhenAll(tasks);
    testStopwatch.Stop();

    Console.WriteLine($"\n=== Test Results ===");
    Console.WriteLine($"Total time: {testStopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Successful requests: {successCount}");
    Console.WriteLine($"Failed requests: {errorCount}");

    if (latencies.Count > 0)
    {
        Console.WriteLine($"Avg latency: {latencies.Average():F2}ms");
        Console.WriteLine($"Min latency: {latencies.Min():F2}ms");
        Console.WriteLine($"Max latency: {latencies.Max():F2}ms");
        Console.WriteLine($"Throughput: {(double)successCount / (testStopwatch.ElapsedMilliseconds / 1000):F2} req/sec");
    }
}
catch (Exception e)
{
    Console.WriteLine($"Test Error: {e}");
}

