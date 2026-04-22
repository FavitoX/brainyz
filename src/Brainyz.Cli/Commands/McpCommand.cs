// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.Text.Json;
using Brainyz.Cli.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz mcp</c> — run brainyz as an MCP server over stdio. The same
/// binary powers the CLI and the server per D-014; agents (Claude Code,
/// Cursor, …) spawn this subcommand and talk JSON-RPC over stdin/stdout.
/// </summary>
public static class McpCommand
{
    public static Command Build()
    {
        var cmd = new Command("mcp", "Run brainyz as an MCP server over stdio");
        cmd.SetAction(async (pr, ct) => await RunAsync(ct));
        return cmd;
    }

    private static async Task<int> RunAsync(CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder();

        // MCP communicates over stdout; every log line MUST go to stderr or
        // we'll corrupt the JSON-RPC framing.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Open the brain once at startup and share the handle across tool
        // calls. BrainContext is thread-safe enough for sequential MCP tool
        // dispatch; concurrent writes across sessions are handled by LibSQL.
        builder.Services.AddSingleton<BrainContext>(
            _ => BrainContext.OpenAsync(ct).GetAwaiter().GetResult());

        // The SDK serializes tool I/O with AOT-safe options that only know
        // about the built-in MCP protocol types. Chain the SDK's resolver
        // with our source-generated context so BrainTools DTOs round-trip.
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolverChain =
            {
                McpJsonUtilities.DefaultOptions.TypeInfoResolver!,
                BrainyzJsonContext.Default,
            },
        };

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<BrainTools>(serializerOptions);

        var host = builder.Build();
        await host.RunAsync(ct);
        return 0;
    }
}
