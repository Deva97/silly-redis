using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Buffers;
using System.Collections.Concurrent;


var registery = new ConcurrentDictionary<string, string>();
var ipAddress = IPAddress.Parse("127.0.0.1");
var server = new TcpListener(ipAddress, 6379);
server.Start();

var ClientListHandlers = new List<Task>();

var source = new CancellationTokenSource();
var token = source.Token;

var failedTask = new List<string>();

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
        "ECHO" => EncodeBulkString(args[1..]),
        "SET" => registery.ContainsKey(args[1]) ? "-ERR failed to set value - KEY EXISTS\r\n" : registery.TryAdd(args[1], args[2]) ? "+OK\r\n" : "-ERR failed to set value - UNKNOWN ERROR\r\n",
        "GET" => registery.TryGetValue(args[1], out var value) ? EncodeBulkString([value]) : "$-1\r\n",
        _ => "-ERR unknown command\r\n"
    };
}

//Parse RESP protocol request
string ParseResp(string request)
{
    var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).
                Where(lines => !(lines.StartsWith('$') || lines.StartsWith('*'))).
                Select(lines => lines.Trim());

    return string.Join(' ', lines);
}

try
{
    while (!token.IsCancellationRequested)
    {
        var acceptClinet = await server.AcceptTcpClientAsync(token);

        //fire and forget. not using await her since waiting make the system to
        // to behave in such a way that it can handle only one client at a time. 
        // we want to handle multiple clients concurrently.
        _ = HandleClientAsync(acceptClinet, token);

    }
}
catch(Exception e)
{
    failedTask.Add($"Server stopped: {e.Message}");
}
finally
{
    source.Cancel();
    server.Stop();
    System.Console.WriteLine("Server stopped.");
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
            if (bytesRead == 0) {System.Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Client disconnected no data"); break;}
            var request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
            var decodedRequest = ParseResp(request);
            Console.WriteLine($"[Client {client.Client.RemoteEndPoint}] Received: {decodedRequest}");

            //respind to client
            var message = Response(decodedRequest.Split(' '));
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