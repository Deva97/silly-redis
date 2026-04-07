using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using SillyRedis;
using SillyRedis.DataStructures;

var registery = new ConcurrentDictionary<string, CachedValue<object>>();
// Per-key lock objects — acquired before any read-modify-write on a key's value.
var keyLocks = new ConcurrentDictionary<string, object>();

var ipAddress = IPAddress.Parse("127.0.0.1");
var listener = new TcpListener(ipAddress, 6379);
listener.Start();

var source = new CancellationTokenSource();
var token = source.Token;

object GetKeyLock(string key) => keyLocks.GetOrAdd(key, _ => new object());

var redisList = new RedisList(registery, GetKeyLock);

//Response based on command
string Response(string[] args)
{
    var command = args[0].ToUpper();
    return command switch
    {
        "PING" => RESProtocol.EncodeSimpleString("PONG"),
        "ECHO" => RESProtocol.EncodeSimpleString(string.Join(' ', args[1..])),
        "SET" => setCachedValue(args[1..]),
        "GET" => GetCachedResponse(args[1]),
        "RPUSH" => RESProtocol.EncodeInteger(redisList.CreateOrAppend(args[1], args[2..], 0)),
        "LPUSH" => RESProtocol.EncodeInteger(redisList.CreateOrAppend(args[1], args[2..], 1)),
        "LRANGE" => RESProtocol.EncodeArray(redisList.Range(args[1], int.Parse(args[2]), int.Parse(args[3]))),
        "LLEN" => RESProtocol.EncodeInteger(redisList.Length(args[1])),
        "LPOP" => redisList.Pop(args[1], args.Length > 2 ? int.Parse(args[2]) : 1),
        _ => RESProtocol.EncodeError("ERR unknown command")
    };
}


string GetCachedResponse(string key)
{
    var cached = getCachedValue(key);
    return cached != null ? RESProtocol.EncodeBulkString(cached.Value?.ToString() ?? string.Empty) : RESProtocol.EncodeNullBulkString();
}

var server = new Server(listener, Response);

try
{
    await server.RunAsync(token);
}
catch(Exception e)
{
    Console.WriteLine($"Server stopped: {e.Message}");
}
finally
{
    source.Cancel();
    listener.Stop();
    System.Console.WriteLine("Server stopped.");
}


string setCachedValue(string[] args)
{
    string success = RESProtocol.EncodeSimpleString("OK");
    string error = RESProtocol.EncodeError("ERR key already exists and is not expired");
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