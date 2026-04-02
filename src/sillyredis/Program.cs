using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Win32;


var registery = new ConcurrentDictionary<string, CachedValue<object>>();
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
    var command = args[0].ToUpper();
    return command switch
    {
        "PING" => "+PONG\r\n",
        "ECHO" => EncodeBulkString(args[1..]),
        "SET" => setCachedValue(args[1..]),
        "GET" => GetCachedResponse(args[1]),
        "RPUSH" => CreateOrAppendList(args[1], args[2..], 0).ToString() + "\r\n",
        "LPUSH" => CreateOrAppendList(args[1], args[2..], 1).ToString() + "\r\n",
        "LRANGE" => EncodeBulkString(getList(args)),
        "LLEN" => ListLength(args[1]).ToString() + "\r\n",
        "LPOP" => RemoveElementList(args[1], args.Length > 2 ? int.Parse(args[2]) : 1),
        _ => "-ERR unknown command\r\n"
    };
}

string RemoveElementList(string key, int count = 1)
{
    if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
    {
        var elements = existingList.Take(Math.Min(count, existingList.Count)).ToList();
        existingList.RemoveRange(0, elements.Count);
        registery.AddOrUpdate(key, new CachedValue<object>(existingList, DateTime.MaxValue), (k, v) => new CachedValue<object>(existingList, DateTime.MaxValue));
        return string.Join("\r\n", elements) + "\r\n";
    }
    return "null\r\n"; // Return null if list not found
}

int ListLength(string key)
{
    if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
    {
        return existingList.Count;
    }
    return 0; // Return 0 if list not found
}

string GetCachedResponse(string key)
{
    var cached = getCachedValue(key);
    return cached != null ? EncodeBulkString(new string[] { cached.Value?.ToString() ?? string.Empty }) : "$-1\r\n";
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
            if (bytesRead == 0) break;

            // Read request and respond with PONG
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


 string setCachedValue(string[] args)
{
    string success = "Key set successfully\r\n";
    string error = "Key already exists and is not expired\r\n";
    var key = args[0];
    var value = args[1];
    if(args.Length < 4)
    {
        registery[key] = new CachedValue<object>(value, DateTime.MaxValue);
        return success; // Key set successfully without expiration
    }

    var ttl = int.Parse(args[3]);
    var format = args[2];
    ttl = format.ToUpper() == "PX" ? ttl : ttl * 1000; // Convert to milliseconds if format is seconds

    var expiry = DateTime.UtcNow.AddMilliseconds(ttl);
    if(registery.TryGetValue(key, out var existingValue))
    {
        if(existingValue.Expiry > DateTime.UtcNow)
        {
            return error; // Key exists and is not expired
        }
    }
    registery[key] = new CachedValue<object>(value, expiry);
    return success; // Key set successfully
}

CachedValue<object>? getCachedValue(string key)
{
    if(registery.TryGetValue(key, out var existingValue))
    {
    
            if(existingValue.Expiry > DateTime.UtcNow)
            {
                return existingValue; // Key exists and is not expired
            }
            else
            {
                registery.TryRemove(key, out _); // Key is expired, remove it
            }
        
    }
    return null; // Key does not exist
}

// Add List to the registry

int CreateOrAppendList(string key, string[] value, int reverted)
{
    if(reverted == 1)
    {
        Array.Reverse(value);
    }

    var list = registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList
        ? existingList
        : new List<string>();
        lock (list)
        {
            list.AddRange(value);
            registery.AddOrUpdate(key, new CachedValue<object>(list, DateTime.MaxValue), (k, v) => new CachedValue<object>(list, DateTime.MaxValue));
            return list.Count; // Return the new length of the list
        }
}

string[] getList(string[] args)
{
    // Implementation for retrieving a range of items from a list
    var key = args[1];
    var start = int.Parse(args[2]);
    var end = int.Parse(args[3]);

    if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
    {
        if(start < 0) start = existingList.Count + start;
        if(end < 0) end = existingList.Count + end;

        lock (existingList)
        {
            return existingList.GetRange(start, Math.Min(end - start +1 , existingList.Count)).ToArray();
        }
    }

    return []; // Return empty array if list not found
}

public class CachedValue<T>
{
    public T Value { get; set; }
    public DateTime Expiry { get; set; }
    public CachedValue(T value, DateTime expiry)
    {
        Value = value;
        Expiry = expiry;
    }
}