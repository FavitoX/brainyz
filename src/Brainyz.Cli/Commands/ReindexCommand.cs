// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core.Embeddings;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz reindex</c> — walks the store and regenerates embeddings for
/// every decision, principle, and note. The content-hash check skips rows
/// already up to date, so this is cheap to run repeatedly.
/// </summary>
public static class ReindexCommand
{
    public static Command Build()
    {
        var cmd = new Command("reindex", "Regenerate embeddings for every entity that needs it");
        cmd.SetAction(async (pr, ct) => await RunAsync(ct));
        return cmd;
    }

    private static async Task<int> RunAsync(CancellationToken ct)
    {
        await using var ctx = await BrainContext.OpenAsync(ct);

        int indexed = 0, upToDate = 0, skipped = 0;

        foreach (var d in await ctx.Store.ListAllDecisionsAsync(limit: int.MaxValue, ct: ct))
            Record(await ctx.Embeddings.IndexDecisionAsync(d, ct), ref indexed, ref upToDate, ref skipped);

        foreach (var p in await ctx.Store.ListAllPrinciplesAsync(activeOnly: false, ct: ct))
            Record(await ctx.Embeddings.IndexPrincipleAsync(p, ct), ref indexed, ref upToDate, ref skipped);

        foreach (var n in await ctx.Store.ListAllNotesAsync(activeOnly: false, limit: int.MaxValue, ct: ct))
            Record(await ctx.Embeddings.IndexNoteAsync(n, ct), ref indexed, ref upToDate, ref skipped);

        Console.WriteLine($"indexed : {indexed}");
        Console.WriteLine($"unchanged: {upToDate}");
        if (skipped > 0)
            Console.WriteLine($"skipped  : {skipped}  (ollama unreachable at {ctx.EmbeddingConfig.Host})");

        return skipped > 0 ? 2 : 0;
    }

    private static void Record(IndexResult r, ref int indexed, ref int upToDate, ref int skipped)
    {
        switch (r)
        {
            case IndexResult.Indexed: indexed++; break;
            case IndexResult.UpToDate: upToDate++; break;
            case IndexResult.ProviderUnavailable: skipped++; break;
        }
    }
}
