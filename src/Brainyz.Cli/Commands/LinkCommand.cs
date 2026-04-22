// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz link &lt;from-id&gt; &lt;relation&gt; &lt;to-id&gt;</c> — create a
/// cross-entity link. Entity types are auto-detected from the ids. The
/// relation accepts snake_case (<c>relates_to</c>) or kebab-case
/// (<c>relates-to</c>) for typing convenience.
/// </summary>
public static class LinkCommand
{
    public static Command Build()
    {
        var cmd = new Command("link", "Link two entities with a typed relation");

        var fromArg = new Argument<string>("from-id")  { Description = "Source entity id" };
        var relArg  = new Argument<string>("relation") { Description = "supersedes | relates_to | depends_on | conflicts_with | informed_by | derived_from | split_from | contradicts" };
        var toArg   = new Argument<string>("to-id")    { Description = "Target entity id" };
        var annOpt  = new Option<string?>("--annotation") { Description = "Free-text context for the link" };

        cmd.Arguments.Add(fromArg);
        cmd.Arguments.Add(relArg);
        cmd.Arguments.Add(toArg);
        cmd.Options.Add(annOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var fromId = pr.GetValue(fromArg)!;
            var toId = pr.GetValue(toArg)!;
            var relationRaw = pr.GetValue(relArg)!;
            var annotation = pr.GetValue(annOpt);

            if (!TryParseRelation(relationRaw, out var relation, out var relErr))
            {
                Console.Error.WriteLine($"error: {relErr}");
                return 2;
            }

            await using var ctx = await BrainContext.OpenAsync(ct);

            var fromType = await ctx.Store.FindEntityByIdAsync(fromId, ct);
            if (fromType is null) { Console.Error.WriteLine($"error: no entity with id {fromId}"); return 1; }
            var toType = await ctx.Store.FindEntityByIdAsync(toId, ct);
            if (toType is null)   { Console.Error.WriteLine($"error: no entity with id {toId}"); return 1; }

            var link = new Link(
                Id: Ids.NewUlid(),
                FromType: fromType.Value,
                FromId: fromId,
                ToType: toType.Value,
                ToId: toId,
                Relation: relation,
                Annotation: annotation);

            await ctx.Store.AddLinkAsync(link, ct);
            Console.WriteLine(link.Id);
            return 0;
        });

        return cmd;
    }

    private static bool TryParseRelation(string raw, out LinkRelation relation, out string? error)
    {
        error = null;
        var normalized = raw.Trim().ToLowerInvariant().Replace('-', '_');
        switch (normalized)
        {
            case "supersedes":      relation = LinkRelation.Supersedes;     return true;
            case "relates_to":      relation = LinkRelation.RelatesTo;      return true;
            case "depends_on":      relation = LinkRelation.DependsOn;      return true;
            case "conflicts_with":  relation = LinkRelation.ConflictsWith;  return true;
            case "informed_by":     relation = LinkRelation.InformedBy;     return true;
            case "derived_from":    relation = LinkRelation.DerivedFrom;    return true;
            case "split_from":      relation = LinkRelation.SplitFrom;      return true;
            case "contradicts":     relation = LinkRelation.Contradicts;    return true;
        }
        relation = LinkRelation.RelatesTo;
        error = $"unknown relation '{raw}' (expected: supersedes | relates_to | depends_on | conflicts_with | informed_by | derived_from | split_from | contradicts)";
        return false;
    }
}
