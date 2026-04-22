// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core.Models;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz delete &lt;id&gt; [--force]</c> — remove an entity by id. The
/// type (decision / principle / note / link) is detected automatically.
/// Cascades through schema triggers: alternatives, embeddings, tag bindings
/// and links all fall along with their owner. A history snapshot is kept
/// automatically via the per-entity AFTER DELETE triggers.
/// </summary>
public static class DeleteCommand
{
    public static Command Build()
    {
        var cmd = new Command("delete", "Delete a decision, principle, note or link by id");

        var idArg = new Argument<string>("id") { Description = "ULID of the entity to delete" };
        var forceOpt = new Option<bool>("--force", "-f") { Description = "Skip the confirmation prompt" };
        cmd.Arguments.Add(idArg);
        cmd.Options.Add(forceOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var id = pr.GetValue(idArg)!;
            var force = pr.GetValue(forceOpt);
            await using var ctx = await BrainContext.OpenAsync(ct);

            // Resolve the type: entity first, link second (links have their
            // own id space and FindEntityByIdAsync only covers entities).
            var entity = await ctx.Store.FindEntityByIdAsync(id, ct);
            string? label = entity switch
            {
                LinkEntity.Decision => "decision",
                LinkEntity.Principle => "principle",
                LinkEntity.Note => "note",
                _ => null,
            };

            bool isLink = false;
            if (label is null)
            {
                var linkMatches = await ctx.Store.GetLinksAsync(ct: ct);
                if (linkMatches.Any(l => l.Id == id)) { label = "link"; isLink = true; }
            }

            if (label is null)
            {
                Console.Error.WriteLine($"error: no entity or link with id {id}");
                return 1;
            }

            if (!force && !Confirm($"delete {label} {id}?"))
            {
                Console.WriteLine("cancelled");
                return 0;
            }

            bool removed = label switch
            {
                "decision" => await ctx.Store.DeleteDecisionAsync(id, ct),
                "principle" => await ctx.Store.DeletePrincipleAsync(id, ct),
                "note" => await ctx.Store.DeleteNoteAsync(id, ct),
                "link" => await ctx.Store.DeleteLinkAsync(id, ct),
                _ => false,
            };
            _ = isLink; // currently only used for the label lookup above

            if (!removed)
            {
                Console.Error.WriteLine($"error: delete failed for {label} {id}");
                return 1;
            }
            Console.WriteLine($"deleted {label} {id}");
            return 0;
        });

        return cmd;
    }

    private static bool Confirm(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            // Non-interactive invocation: default to deny rather than risk
            // a silent destructive action.
            Console.Error.WriteLine($"{prompt} [y/N]: (declined — rerun with --force for non-interactive delete)");
            return false;
        }
        Console.Write($"{prompt} [y/N]: ");
        var line = Console.ReadLine();
        return line?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
            || line?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }
}
