using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Buffers;
using System.Collections.Concurrent;

var registery = new ConcurrentDictionary<string, CachedValue<object>>();
// Per-key lock objects — acquired before any read-modify-write on a key's value.
var keyLocks = new ConcurrentDictionary<string, object>();

var ipAddress = IPAddress.Parse("127.0.0.1");
var server = new TcpListener(ipAddress, 6379);
server.Start();

var source = new CancellationTokenSource();
var token = source.Token;

object GetKeyLock(string key) => keyLocks.GetOrAdd(key, _ => new object());

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
    lock (GetKeyLock(key))
    {
        if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
        {
            int removeCount = Math.Min(count, existingList.Count);
            var elements = existingList.GetRange(0, removeCount);
            existingList.RemoveRange(0, removeCount);
            return string.Join("\r\n", elements) + "\r\n";
        }
        return "null\r\n";
    }
}

int ListLength(string key)
{
    lock (GetKeyLock(key))
    {
        if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
        {
            return existingList.Count;
        }
        return 0;
    }
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
    Console.WriteLine($"Server stopped: {e.Message}");
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

    if (args.Length < 4)
    {
        registery[key] = new CachedValue<object>(value, DateTime.MaxValue);
        return success;
    }

    var ttl = int.Parse(args[3]);
    var format = args[2];
    ttl = format.ToUpper() == "PX" ? ttl : ttl * 1000;
    var expiry = DateTime.UtcNow.AddMilliseconds(ttl);

    // Lock to make the expiry-check + write atomic.
    lock (GetKeyLock(key))
    {
        if (registery.TryGetValue(key, out var existingValue) && existingValue.Expiry > DateTime.UtcNow)
        {
            return error;
        }
        registery[key] = new CachedValue<object>(value, expiry);
        return success;
    }
}

CachedValue<object>? getCachedValue(string key)
{
    if (registery.TryGetValue(key, out var existingValue))
    {
        if (existingValue.Expiry > DateTime.UtcNow)
        {
            return existingValue;
        }
        // Only remove the entry if it is still the same (expired) value we just read,
        // preventing a race where a concurrent SET wrote a fresh value in between.
        registery.TryRemove(new KeyValuePair<string, CachedValue<object>>(key, existingValue));
    }
    return null;
}

// Add List to the registry
//need to look into the logic.
int CreateOrAppendList(string key, string[] value, int reverted)
{
    if (reverted == 1) Array.Reverse(value);

    // Acquire the per-key lock before the lookup so that the check-then-create
    // is atomic. Without this, two threads can both see a missing key, each
    // create their own List<string>, and then race on AddOrUpdate — losing one
    // thread's items entirely.
    lock (GetKeyLock(key))
    {
        List<string> list;
        if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
        {
            list = existingList;
        }
        else
        {
            list = [];
            registery[key] = new CachedValue<object>(list, DateTime.MaxValue);
        }
        list.AddRange(value);
        return list.Count;
    }
}

string[] getList(string[] args)
{
    var key = args[1];
    var start = int.Parse(args[2]);
    var end = int.Parse(args[3]);

    lock (GetKeyLock(key))
    {
        if (registery.TryGetValue(key, out var existingValue) && existingValue.Value is List<string> existingList)
        {
            // Resolve negative indices inside the lock so Count is stable.
            int count = existingList.Count;
            if (start < 0) start = count + start;
            if (end < 0) end = count + end;
            start = Math.Max(0, start);
            end = Math.Min(count - 1, end);
            if (start > end) return [];
            return [.. existingList.GetRange(start, end - start + 1)];
        }
    }

    return [];
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