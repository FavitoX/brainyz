// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli.Output;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz search &lt;query&gt;</c> — hybrid search across decisions,
/// principles and notes. Fuses FTS5 (text match) with vector similarity
/// (semantic) when embeddings are available; falls back to FTS5 alone
/// when Ollama is unreachable.
/// </summary>
public static class SearchCommand
{
    public static Command Build()
    {
        var cmd = new Command("search", "Hybrid FTS5 + semantic search across decisions, principles and notes");

        var queryArg = new Argument<string>("query") { Description = "Search expression (plain words or FTS5 syntax)" };
        var limitOpt = new Option<int>("--limit") { Description = "Max rows in the final ranking (default: 10)", DefaultValueFactory = _ => 10 };
        var textOnlyOpt = new Option<bool>("--text-only") { Description = "Skip the semantic component; use FTS5 only" };

        cmd.Arguments.Add(queryArg);
        cmd.Options.Add(limitOpt);
        cmd.Options.Add(textOnlyOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var query = pr.GetValue(queryArg)!;
            var limit = pr.GetValue(limitOpt);
            var textOnly = pr.GetValue(textOnlyOpt);

            await using var ctx = await BrainContext.OpenAsync(ct);

            if (textOnly)
            {
                foreach (var d in await ctx.Store.SearchDecisionsAsync(query, limit, ct))
                    Render.SearchHit("decision", d.Id, d.ProjectId, d.Title);
                foreach (var p in await ctx.Store.SearchPrinciplesAsync(query, limit, ct))
                    Render.SearchHit("principle", p.Id, p.ProjectId, p.Title);
                foreach (var n in await ctx.Store.SearchNotesAsync(query, limit, ct))
                    Render.SearchHit("note", n.Id, n.ProjectId, n.Title);
                return 0;
            }

            var hits = await ctx.Search.SearchAsync(query, finalLimit: limit, ct: ct);
            foreach (var h in hits)
                Render.SearchHit(h.Type, h.Id, h.ProjectId, h.Title);
            return 0;
        });

        return cmd;
    }
}
