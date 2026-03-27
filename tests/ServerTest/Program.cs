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


//Ecode Bulk String for RESP protocol
string EncodeBulkString(string[] args)
{
    var sb = new StringBuilder();
    sb.Append($"*{args.Length}\r\n");
    foreach (var arg in args)
    {
        sb.Append($"${arg.Length}\r\n{arg}\r\n");
    }

    return sb.ToString();
}

//Response based on command
string Response(string[] args)
{
    return args[0].ToUpper() switch
    {
        "PING" => "+PONG\r\n",
        "ECHO" => EncodeBulkString(args),
        _ => "-ERR unknown command\r\n"
    };
}

//Parse RESP protocol request
string ParseResp(string request)
{
    var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).
                Where(lines => !(lines.StartsWith('$') || lines.StartsWith('*'))).
                Select(lines => lines.Trim()).ToArray();

    return string.Join(' ', lines);
}


async Task ClientWorker(int clientId)
{
    var source = new CancellationTokenSource();
    var token = source.Token;

    using var client = new TcpClient();
    try
    {
       
        await client.ConnectAsync(ipAddress, 6379);
        Console.WriteLine($"[Client {clientId:D2}] Connected");

        await using var stream = client.GetStream();

        for (int req = 1; req <= requestsPerClient; req++)
        {
            var message = $"ECHO Hello World";
            var messageBytes = Encoding.UTF8.GetBytes(Response(message.Split(' ')));
            await stream.WriteAsync(messageBytes, token);
            await stream.FlushAsync(token);

            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, token);

            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            lock (latencies)
            {
                successCount++;
            }

            var parsedResponse = ParseResp(response);

            Console.WriteLine($"[Client {clientId:D2}] Request {req}: Received '{parsedResponse}'");
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
    finally
    {
        source.Cancel();
        client.Close();
        Console.WriteLine($"[Client {clientId:D2}] Disconnected");
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

