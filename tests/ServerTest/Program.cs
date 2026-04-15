using System.Net;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("=== silly-redis Integration Test Suite ===\n");

var ipAddress = IPAddress.Parse("127.0.0.1");
const int port = 6379;

int passed = 0;
int total  = 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

void Assert(bool condition, string testName)
{
    total++;
    if (condition)
    {
        passed++;
        Console.WriteLine($"  [PASS] {testName}");
    }
    else
    {
        Console.WriteLine($"  [FAIL] {testName}");
    }
}

string EncodeBulkString(string[] args)
{
    var sb = new StringBuilder();
    sb.Append($"*{args.Length}\r\n");
    foreach (var arg in args)
        sb.Append($"${arg.Length}\r\n{arg}\r\n");
    return sb.ToString();
}

async Task<string> SendAndReceive(NetworkStream stream, string[] args)
{
    var bytes = Encoding.UTF8.GetBytes(EncodeBulkString(args));
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();

    // Read until we have a complete RESP message
    using var acc = new MemoryStream();
    var buf = new byte[4096];
    do
    {
        var n = await stream.ReadAsync(buf);
        if (n == 0) break;
        acc.Write(buf, 0, n);
    } while (stream.DataAvailable);

    return Encoding.UTF8.GetString(acc.ToArray());
}

(TcpClient client, NetworkStream stream) Connect()
{
    var c = new TcpClient();
    c.Connect(ipAddress, port);
    return (c, c.GetStream());
}

// ── Sequential Tests ──────────────────────────────────────────────────────────

async Task RunSequentialTests()
{
    // ── 1. PING ───────────────────────────────────────────────────────────────
    Console.WriteLine("\n[PING]");
    {
        var (client, stream) = Connect();
        var r = await SendAndReceive(stream, ["PING"]);
        Assert(r == "+PONG\r\n", "PING returns +PONG");
        client.Close();
    }

    // ── 2. ECHO ───────────────────────────────────────────────────────────────
    Console.WriteLine("\n[ECHO]");
    {
        var (client, stream) = Connect();
        var r = await SendAndReceive(stream, ["ECHO", "hello", "world"]);
        Assert(r == "+hello world\r\n", "ECHO joins args with space");
        client.Close();
    }

    // ── 3. SET / GET — basic ──────────────────────────────────────────────────
    Console.WriteLine("\n[SET / GET basic]");
    {
        var (client, stream) = Connect();
        var set = await SendAndReceive(stream, ["SET", "foo", "bar"]);
        Assert(set == "+OK\r\n", "SET returns +OK");

        var get = await SendAndReceive(stream, ["GET", "foo"]);
        Assert(get == "$3\r\nbar\r\n", "GET returns stored value");

        var miss = await SendAndReceive(stream, ["GET", "nonexistent_key_xyz"]);
        Assert(miss == "$-1\r\n", "GET non-existent returns null bulk string");
        client.Close();
    }

    // ── 4. SET PX — TTL expiry ────────────────────────────────────────────────
    Console.WriteLine("\n[SET PX expiry]");
    {
        var (client, stream) = Connect();
        await SendAndReceive(stream, ["SET", "px_key", "val", "PX", "150"]);

        var before = await SendAndReceive(stream, ["GET", "px_key"]);
        Assert(before == "$3\r\nval\r\n", "GET before PX expiry returns value");

        await Task.Delay(250);
        var after = await SendAndReceive(stream, ["GET", "px_key"]);
        Assert(after == "$-1\r\n", "GET after PX expiry returns null");
        client.Close();
    }

    // ── 5. SET EX — TTL expiry ────────────────────────────────────────────────
    Console.WriteLine("\n[SET EX expiry]");
    {
        var (client, stream) = Connect();
        await SendAndReceive(stream, ["SET", "ex_key", "val", "EX", "1"]);

        await Task.Delay(1100);
        var after = await SendAndReceive(stream, ["GET", "ex_key"]);
        Assert(after == "$-1\r\n", "GET after EX expiry returns null");
        client.Close();
    }

    // ── 6. SET — fail on non-expired existing key ─────────────────────────────
    Console.WriteLine("\n[SET collision]");
    {
        var (client, stream) = Connect();
        await SendAndReceive(stream, ["SET", "locked_key", "val1"]);
        var second = await SendAndReceive(stream, ["SET", "locked_key", "val2"]);
        Assert(second.StartsWith("-ERR"), "SET on non-expired key returns error");

        var get = await SendAndReceive(stream, ["GET", "locked_key"]);
        Assert(get == "$4\r\nval1\r\n", "Original value preserved after failed SET");
        client.Close();
    }

    // ── 7. Unknown command ────────────────────────────────────────────────────
    Console.WriteLine("\n[Unknown command]");
    {
        var (client, stream) = Connect();
        var r = await SendAndReceive(stream, ["UNKNOWN", "arg"]);
        Assert(r.StartsWith("-ERR unknown command"), "Unknown command returns error");
        client.Close();
    }

    // ── 8. RPUSH / LLEN / LRANGE ─────────────────────────────────────────────
    Console.WriteLine("\n[RPUSH / LLEN / LRANGE]");
    {
        var (client, stream) = Connect();

        var rpush = await SendAndReceive(stream, ["RPUSH", "mylist", "a", "b", "c"]);
        Assert(rpush == ":3\r\n", "RPUSH returns new list length");

        var llen = await SendAndReceive(stream, ["LLEN", "mylist"]);
        Assert(llen == ":3\r\n", "LLEN returns correct length");

        var full = await SendAndReceive(stream, ["LRANGE", "mylist", "0", "-1"]);
        Assert(full == "*3\r\n$1\r\na\r\n$1\r\nb\r\n$1\r\nc\r\n", "LRANGE 0 -1 returns all elements");

        var partial = await SendAndReceive(stream, ["LRANGE", "mylist", "0", "1"]);
        Assert(partial == "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "LRANGE 0 1 returns first two");

        var negIdx = await SendAndReceive(stream, ["LRANGE", "mylist", "-2", "-1"]);
        Assert(negIdx == "*2\r\n$1\r\nb\r\n$1\r\nc\r\n", "LRANGE with negative indices");

        var outOfRange = await SendAndReceive(stream, ["LRANGE", "mylist", "5", "10"]);
        Assert(outOfRange == "*0\r\n", "LRANGE out-of-range returns empty array");

        var noKey = await SendAndReceive(stream, ["LRANGE", "no_such_list", "0", "-1"]);
        Assert(noKey == "*0\r\n", "LRANGE on non-existent key returns empty array");

        var llenMissing = await SendAndReceive(stream, ["LLEN", "no_such_list"]);
        Assert(llenMissing == ":0\r\n", "LLEN on non-existent key returns 0");

        client.Close();
    }

    // ── 9. LPUSH ─────────────────────────────────────────────────────────────
    Console.WriteLine("\n[LPUSH]");
    {
        var (client, stream) = Connect();

        var lpush = await SendAndReceive(stream, ["LPUSH", "mylist2", "a", "b", "c"]);
        Assert(lpush == ":3\r\n", "LPUSH returns new list length");

        // LPUSH reverses values: input [a,b,c] → stored [c,b,a]
        var range = await SendAndReceive(stream, ["LRANGE", "mylist2", "0", "-1"]);
        Assert(range == "*3\r\n$1\r\nc\r\n$1\r\nb\r\n$1\r\na\r\n", "LPUSH stores values in reversed order");

        client.Close();
    }

    // ── 10. LPOP ─────────────────────────────────────────────────────────────
    Console.WriteLine("\n[LPOP]");
    {
        var (client, stream) = Connect();

        await SendAndReceive(stream, ["RPUSH", "poplist", "x", "y", "z"]);

        var single = await SendAndReceive(stream, ["LPOP", "poplist"]);
        Assert(single == "$1\r\nx\r\n", "LPOP single returns first element");

        var multi = await SendAndReceive(stream, ["LPOP", "poplist", "2"]);
        Assert(multi == "*2\r\n$1\r\ny\r\n$1\r\nz\r\n", "LPOP count returns array of elements");

        var empty = await SendAndReceive(stream, ["LPOP", "poplist"]);
        Assert(empty == "$-1\r\n", "LPOP on empty list returns null bulk string");

        var missingOne = await SendAndReceive(stream, ["LPOP", "no_such_pop_list"]);
        Assert(missingOne == "$-1\r\n", "LPOP count=1 on non-existent key returns null bulk string");

        var missingMany = await SendAndReceive(stream, ["LPOP", "no_such_pop_list", "3"]);
        Assert(missingMany == "*0\r\n", "LPOP count>1 on non-existent key returns empty array");

        client.Close();
    }

    // ── 11. LPOP partial (pop more than available) ────────────────────────────
    Console.WriteLine("\n[LPOP partial]");
    {
        var (client, stream) = Connect();

        await SendAndReceive(stream, ["RPUSH", "partiallist", "a", "b"]);
        var partial = await SendAndReceive(stream, ["LPOP", "partiallist", "10"]);
        Assert(partial == "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "LPOP count > length clamps to available elements");

        client.Close();
    }

    // ── 12. TYPE ─────────────────────────────────────────────────────────────
    Console.WriteLine("\n[TYPE]");
    {
        var (client, stream) = Connect();

        // non-existent key → "none"
        var none = await SendAndReceive(stream, ["TYPE", "type_no_such_key"]);
        Assert(none == "+none\r\n", "TYPE on non-existent key returns none");

        // string key
        await SendAndReceive(stream, ["SET", "type_str_key", "hello"]);
        var strType = await SendAndReceive(stream, ["TYPE", "type_str_key"]);
        Assert(strType == "+System.String\r\n", "TYPE on string key returns System.String");

        // list key
        await SendAndReceive(stream, ["RPUSH", "type_list_key", "a", "b"]);
        var listType = await SendAndReceive(stream, ["TYPE", "type_list_key"]);
        Assert(listType == "+System.Collections.Generic.List`1[System.String]\r\n", "TYPE on list key returns List type");

        // expired key → "none"
        await SendAndReceive(stream, ["SET", "type_exp_key", "val", "PX", "100"]);
        await Task.Delay(200);
        var expiredType = await SendAndReceive(stream, ["TYPE", "type_exp_key"]);
        Assert(expiredType == "+none\r\n", "TYPE on expired key returns none");

        client.Close();
    }
}

// ── Concurrency Tests ─────────────────────────────────────────────────────────

async Task RunConcurrencyTests()
{
    // Scenario A: RPUSH race — 20 clients each push 10 items to the same list
    Console.WriteLine("\n[Concurrency A: RPUSH race — 20 clients × 10 pushes]");
    {
        const int clients = 20;
        const int pushesEach = 10;

        var tasks = Enumerable.Range(0, clients).Select(async i =>
        {
            var (client, stream) = Connect();
            try
            {
                for (int j = 0; j < pushesEach; j++)
                    await SendAndReceive(stream, ["RPUSH", "shared_list", $"item_{i}_{j}"]);
            }
            finally { client.Close(); }
        });

        await Task.WhenAll(tasks);

        var (verifier, vStream) = Connect();
        var llen = await SendAndReceive(vStream, ["LLEN", "shared_list"]);
        Assert(llen == $":{clients * pushesEach}\r\n", $"RPUSH race: LLEN equals {clients * pushesEach} after concurrent pushes");
        verifier.Close();
    }

    // Scenario B: SET race — 10 clients race to SET the same key (no TTL)
    // First writer wins; all others get an error. The key must hold a valid value.
    Console.WriteLine("\n[Concurrency B: SET race — 10 clients racing on the same key]");
    {
        const int racers = 10;
        int okCount    = 0;
        int errCount   = 0;
        var lockObj    = new object();

        var tasks = Enumerable.Range(0, racers).Select(async i =>
        {
            var (client, stream) = Connect();
            try
            {
                var r = await SendAndReceive(stream, ["SET", "counter_key", $"racer_{i}"]);
                lock (lockObj)
                {
                    if (r == "+OK\r\n") okCount++;
                    else if (r.StartsWith("-ERR")) errCount++;
                }
            }
            finally { client.Close(); }
        });

        await Task.WhenAll(tasks);

        Assert(okCount == 1,   "SET race: exactly one client wins");
        Assert(errCount == racers - 1, $"SET race: remaining {racers - 1} clients get errors");

        var (verifier, vStream) = Connect();
        var get = await SendAndReceive(vStream, ["GET", "counter_key"]);
        Assert(!get.StartsWith("$-1"), "SET race: key holds a valid value after race");
        verifier.Close();
    }

    // Scenario C: Mixed operations — 20 clients doing RPUSH/LLEN/LRANGE/LPOP concurrently
    Console.WriteLine("\n[Concurrency C: mixed RPUSH/LLEN/LRANGE/LPOP — 20 clients]");
    {
        const int clients = 20;
        int exceptions = 0;
        var lockObj    = new object();

        var tasks = Enumerable.Range(0, clients).Select(async i =>
        {
            var (client, stream) = Connect();
            try
            {
                switch (i % 4)
                {
                    case 0:
                        for (int j = 0; j < 5; j++)
                            await SendAndReceive(stream, ["RPUSH", "mixed_list", $"v{i}_{j}"]);
                        break;
                    case 1:
                        for (int j = 0; j < 5; j++)
                            await SendAndReceive(stream, ["LLEN", "mixed_list"]);
                        break;
                    case 2:
                        for (int j = 0; j < 5; j++)
                            await SendAndReceive(stream, ["LRANGE", "mixed_list", "0", "-1"]);
                        break;
                    case 3:
                        for (int j = 0; j < 5; j++)
                            await SendAndReceive(stream, ["LPOP", "mixed_list", "2"]);
                        break;
                }
            }
            catch
            {
                lock (lockObj) { exceptions++; }
            }
            finally { client.Close(); }
        });

        await Task.WhenAll(tasks);
        Assert(exceptions == 0, "Mixed concurrency: zero exceptions across all 20 clients");
    }
}

// ── Run all ───────────────────────────────────────────────────────────────────

try
{
    await RunSequentialTests();
    await RunConcurrencyTests();
}
catch (Exception e)
{
    Console.WriteLine($"\nFATAL: {e.Message}");
}

int failed = total - passed;
Console.WriteLine($"\n=== Test Results ===");
Console.WriteLine($"Passed: {passed} / {total}");
if (failed > 0) Console.WriteLine($"Failed: {failed}");

Environment.Exit(failed > 0 ? 1 : 0);
