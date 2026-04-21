using System.ComponentModel;
using ModelContextProtocol.Server;
using SillyRedisMcp.RedisClient;

namespace SillyRedisMcp.Tools;

[McpServerToolType]
public sealed class StringTools(IRedisClient redis)
{
    [McpServerTool, Description(
        "Sets a key to a string value. " +
        "If ttlMode is 'EX', ttl is in seconds. If 'PX', ttl is in milliseconds. " +
        "Omit ttlMode and ttl for no expiry. " +
        "Note: this server returns an error if the key already exists and has not expired.")]
    public async Task<string> Set(
        [Description("The key name.")] string key,
        [Description("The string value to store.")] string value,
        [Description("Optional expiry mode: EX (seconds) or PX (milliseconds).")] string? ttlMode = null,
        [Description("Optional TTL value. Required when ttlMode is provided.")] int? ttl = null,
        CancellationToken ct = default)
    {
        string[] args = (ttlMode is not null && ttl is not null)
            ? ["SET", key, value, ttlMode.ToUpper(), ttl.Value.ToString()]
            : ["SET", key, value];

        var result = await redis.SendAsync(args, ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Gets the string value of a key. Returns (nil) if the key does not exist or has expired.")]
    public async Task<string> Get(
        [Description("The key name.")] string key,
        CancellationToken ct = default)
    {
        var result = await redis.SendAsync(["GET", key], ct);
        return ToolHelper.Stringify(result);
    }
}
