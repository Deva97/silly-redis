using System.ComponentModel;
using ModelContextProtocol.Server;
using SillyRedisMcp.RedisClient;

namespace SillyRedisMcp.Tools;

[McpServerToolType]
public sealed class PingEchoTools(IRedisClient redis)
{
    [McpServerTool, Description("Sends a PING to the silly-redis server. Returns PONG if the server is reachable.")]
    public async Task<string> Ping(CancellationToken ct)
    {
        var result = await redis.SendAsync(["PING"], ct);
        return ToolHelper.Stringify(result);
    }

    [McpServerTool, Description("Sends a message to the silly-redis server and echoes it back.")]
    public async Task<string> Echo(
        [Description("The message text to echo.")] string message,
        CancellationToken ct)
    {
        var result = await redis.SendAsync(["ECHO", message], ct);
        return ToolHelper.Stringify(result);
    }
}
