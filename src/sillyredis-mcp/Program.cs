using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SillyRedisMcp.RedisClient;

var builder = Host.CreateApplicationBuilder(args);

// Route all logs to stderr — stdout is reserved exclusively for MCP JSON-RPC framing.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

// Register the Redis client as a singleton and expose it as a hosted service
// so that StartAsync/StopAsync manage the TCP connection lifecycle.
builder.Services.AddSingleton<RedisClient>();
builder.Services.AddSingleton<IRedisClient>(sp => sp.GetRequiredService<RedisClient>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RedisClient>());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

await builder.Build().RunAsync();
