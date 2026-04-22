// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli.Output;
using Brainyz.Core.Models;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz show &lt;id&gt;</c> — print the full body of a decision, principle
/// or note by id. The entity type is inferred via
/// <see cref="Brainyz.Core.Storage.BrainStore.FindEntityByIdAsync"/>.
/// </summary>
public static class ShowCommand
{
    public static Command Build()
    {
        var cmd = new Command("show", "Show a decision, principle or note by id");
        var idArg = new Argument<string>("id") { Description = "ULID of the entity to display" };
        cmd.Arguments.Add(idArg);

        cmd.SetAction(async (pr, ct) =>
        {
            var id = pr.GetValue(idArg)!;
            await using var ctx = await BrainContext.OpenAsync(ct);

            var type = await ctx.Store.FindEntityByIdAsync(id, ct);
            switch (type)
            {
                case LinkEntity.Decision:
                    var d = await ctx.Store.GetDecisionAsync(id, ct);
                    if (d is null) { Console.Error.WriteLine($"error: decision {id} not found"); return 1; }
                    Render.Decision(d);
                    return 0;
                case LinkEntity.Principle:
                    var p = await ctx.Store.GetPrincipleAsync(id, ct);
                    if (p is null) { Console.Error.WriteLine($"error: principle {id} not found"); return 1; }
                    Render.Principle(p);
                    return 0;
                case LinkEntity.Note:
                    var n = await ctx.Store.GetNoteAsync(id, ct);
                    if (n is null) { Console.Error.WriteLine($"error: note {id} not found"); return 1; }
                    Render.Note(n);
                    return 0;
                default:
                    Console.Error.WriteLine($"error: no entity with id {id}");
                    return 1;
            }
        });

        return cmd;
    }
}
