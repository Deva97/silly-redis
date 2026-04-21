using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using SillyRedisMcp.RedisClient;

namespace SillyRedisMcp.Tools;

[McpServerToolType]
public sealed class ListTools(IRedisClient redis)
{
    [McpServerTool, Description("Appends one or more values to the tail (right) of a list. Creates the list if it does not exist. Returns the new list length.")]
    public async Task<string> Rpush(
        [Description("The list key.")] string key,
        [Description("One or more values to append.")] string[] values,
        CancellationToken ct = default)
    {
        var args = new[] { "RPUSH", key }.Concat(values).ToArray();
        var result = await redis.SendAsync(args, ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Prepends one or more values to the head (left) of a list. Creates the list if it does not exist. Returns the new list length.")]
    public async Task<string> Lpush(
        [Description("The list key.")] string key,
        [Description("One or more values to prepend.")] string[] values,
        CancellationToken ct = default)
    {
        var args = new[] { "LPUSH", key }.Concat(values).ToArray();
        var result = await redis.SendAsync(args, ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Returns a range of elements from a list. Index 0 is the first element; negative indices count from the tail (-1 is the last element).")]
    public async Task<string> Lrange(
        [Description("The list key.")] string key,
        [Description("Start index (inclusive, 0-based).")] int start,
        [Description("End index (inclusive). Use -1 for the last element.")] int end,
        CancellationToken ct = default)
    {
        var result = await redis.SendAsync(["LRANGE", key, start.ToString(), end.ToString()], ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Returns the length of a list, or 0 if the key does not exist.")]
    public async Task<string> Llen(
        [Description("The list key.")] string key,
        CancellationToken ct = default)
    {
        var result = await redis.SendAsync(["LLEN", key], ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Removes and returns one or more elements from the head (left) of a list. Returns (nil) if the list is empty or does not exist.")]
    public async Task<string> Lpop(
        [Description("The list key.")] string key,
        [Description("Number of elements to pop. Defaults to 1.")] int count = 1,
        CancellationToken ct = default)
    {
        var args = count == 1
            ? new[] { "LPOP", key }
            : ["LPOP", key, count.ToString()];

        var result = await redis.SendAsync(args, ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description(
        "Blocking left-pop: waits for an element to become available in any of the given keys, then pops it. " +
        "Returns '[key, element]' on success, or (nil) on timeout. " +
        "Timeout is capped at 5 seconds to avoid blocking MCP indefinitely.")]
    public async Task<string> Blpop(
        [Description("One or more list keys to watch.")] string[] keys,
        [Description("Timeout in seconds (0 uses the 5s cap). Maximum is 5 seconds.")] float timeout,
        CancellationToken ct = default)
    {
        float safeTimeout = (timeout <= 0 || timeout > 5f) ? 5f : timeout;
        var args = new[] { "BLPOP" }
            .Concat(keys)
            .Append(safeTimeout.ToString("F1", CultureInfo.InvariantCulture))
            .ToArray();

        var result = await redis.SendAsync(args, ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Returns the type of value stored at a key: 'string', 'list', 'set', 'zset', 'hash', 'stream', 'vectorset', or 'none' if the key does not exist or has expired.")]
    public async Task<string> Type(
        [Description("The key name.")] string key,
        CancellationToken ct = default)
    {
        var result = await redis.SendAsync(["TYPE", key], ct);
        return ToolHelper.Stringify(result);
    }
}
