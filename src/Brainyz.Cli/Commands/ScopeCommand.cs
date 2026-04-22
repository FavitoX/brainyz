// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli.Output;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz scope</c> — prints the resolved project scope for the current
/// working directory. Useful for debugging <c>.brain</c> files, git remotes
/// and config path maps when a query lands in the wrong scope.
/// </summary>
public static class ScopeCommand
{
    public static Command Build()
    {
        var cmd = new Command("scope", "Show the resolved project scope for the current directory");
        cmd.SetAction(async (pr, ct) => await RunAsync(ct));
        return cmd;
    }

    private static async Task<int> RunAsync(CancellationToken ct)
    {
        await using var ctx = await BrainContext.OpenAsync(ct);
        var resolution = await ctx.Resolver.ResolveAsync(Directory.GetCurrentDirectory(), ct);
        Render.ScopeLine(resolution);
        return 0;
    }
}
