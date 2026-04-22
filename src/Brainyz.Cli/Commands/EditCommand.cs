// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core.Models;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz edit (decision|principle|note) &lt;id&gt; [--field …]</c> — patch
/// specific fields of an existing entity. Only fields passed as flags are
/// touched; everything else stays. A successful edit triggers a re-embedding
/// (best-effort) so hybrid search stays consistent.
/// </summary>
public static class EditCommand
{
    public static Command Build()
    {
        var cmd = new Command("edit", "Edit a decision, principle or note");
        cmd.Subcommands.Add(BuildDecision());
        cmd.Subcommands.Add(BuildPrinciple());
        cmd.Subcommands.Add(BuildNote());
        return cmd;
    }

    // ───────── edit decision ─────────

    private static Command BuildDecision()
    {
        var sub = new Command("decision", "Edit a decision by id");

        var idArg = new Argument<string>("id") { Description = "ULID of the decision" };
        var titleOpt = new Option<string?>("--title") { Description = "New title" };
        var textOpt = new Option<string?>("--text") { Description = "New body text (or pipe via stdin)" };
        var contextOpt = new Option<string?>("--context") { Description = "New context / problem statement" };
        var rationaleOpt = new Option<string?>("--rationale") { Description = "New rationale" };
        var consequencesOpt = new Option<string?>("--consequences") { Description = "New consequences" };
        var confidenceOpt = new Option<string?>("--confidence") { Description = "low | medium | high (use 'none' to clear)" };
        var statusOpt = new Option<string?>("--status") { Description = "proposed | accepted | deprecated | superseded" };

        sub.Arguments.Add(idArg);
        foreach (var o in new Option[] { titleOpt, textOpt, contextOpt, rationaleOpt, consequencesOpt, confidenceOpt, statusOpt })
            sub.Options.Add(o);

        sub.SetAction(async (pr, ct) =>
        {
            var id = pr.GetValue(idArg)!;
            await using var ctx = await BrainContext.OpenAsync(ct);
            var current = await ctx.Store.GetDecisionAsync(id, ct);
            if (current is null) { Console.Error.WriteLine($"error: decision {id} not found"); return 1; }

            var newText = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);

            var patched = current with
            {
                Title = pr.GetValue(titleOpt) ?? current.Title,
                Text = newText ?? current.Text,
                Context = pr.GetValue(contextOpt) ?? current.Context,
                Rationale = pr.GetValue(rationaleOpt) ?? current.Rationale,
                Consequences = pr.GetValue(consequencesOpt) ?? current.Consequences,
            };

            var confRaw = pr.GetValue(confidenceOpt);
            if (confRaw is not null)
            {
                if (confRaw.Equals("none", StringComparison.OrdinalIgnoreCase)) patched = patched with { Confidence = null };
                else
                {
                    patched = patched with
                    {
                        Confidence = confRaw.ToLowerInvariant() switch
                        {
                            "low" => Confidence.Low,
                            "medium" => Confidence.Medium,
                            "high" => Confidence.High,
                            _ => throw new ArgumentException($"invalid --confidence '{confRaw}'")
                        }
                    };
                }
            }

            var statusRaw = pr.GetValue(statusOpt);
            if (statusRaw is not null)
            {
                patched = patched with
                {
                    Status = statusRaw.ToLowerInvariant() switch
                    {
                        "proposed" => DecisionStatus.Proposed,
                        "accepted" => DecisionStatus.Accepted,
                        "deprecated" => DecisionStatus.Deprecated,
                        "superseded" => DecisionStatus.Superseded,
                        _ => throw new ArgumentException($"invalid --status '{statusRaw}'")
                    }
                };
            }

            if (!await ctx.Store.UpdateDecisionAsync(patched, ct))
            {
                Console.Error.WriteLine($"error: update failed for decision {id}");
                return 1;
            }
            await ctx.Embeddings.IndexDecisionAsync(patched, ct); // best-effort re-embed
            Console.WriteLine(patched.Id);
            return 0;
        });

        return sub;
    }

    // ───────── edit principle ─────────

    private static Command BuildPrinciple()
    {
        var sub = new Command("principle", "Edit a principle by id");

        var idArg = new Argument<string>("id") { Description = "ULID of the principle" };
        var titleOpt = new Option<string?>("--title") { Description = "New title" };
        var textOpt = new Option<string?>("--text") { Description = "New statement (or pipe via stdin)" };
        var rationaleOpt = new Option<string?>("--rationale") { Description = "New rationale" };
        var activeOpt = new Option<bool?>("--active") { Description = "true to un-archive; false to archive" };

        sub.Arguments.Add(idArg);
        foreach (var o in new Option[] { titleOpt, textOpt, rationaleOpt, activeOpt })
            sub.Options.Add(o);

        sub.SetAction(async (pr, ct) =>
        {
            var id = pr.GetValue(idArg)!;
            await using var ctx = await BrainContext.OpenAsync(ct);
            var current = await ctx.Store.GetPrincipleAsync(id, ct);
            if (current is null) { Console.Error.WriteLine($"error: principle {id} not found"); return 1; }

            var newText = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);
            var patched = current with
            {
                Title = pr.GetValue(titleOpt) ?? current.Title,
                Statement = newText ?? current.Statement,
                Rationale = pr.GetValue(rationaleOpt) ?? current.Rationale,
                Active = pr.GetValue(activeOpt) ?? current.Active,
            };

            if (!await ctx.Store.UpdatePrincipleAsync(patched, ct))
            {
                Console.Error.WriteLine($"error: update failed for principle {id}");
                return 1;
            }
            await ctx.Embeddings.IndexPrincipleAsync(patched, ct);
            Console.WriteLine(patched.Id);
            return 0;
        });

        return sub;
    }

    // ───────── edit note ─────────

    private static Command BuildNote()
    {
        var sub = new Command("note", "Edit a note by id");

        var idArg = new Argument<string>("id") { Description = "ULID of the note" };
        var titleOpt = new Option<string?>("--title") { Description = "New title" };
        var textOpt = new Option<string?>("--text") { Description = "New body content (or pipe via stdin)" };
        var sourceOpt = new Option<string?>("--source") { Description = "New source URL" };
        var activeOpt = new Option<bool?>("--active") { Description = "true to un-archive; false to archive" };

        sub.Arguments.Add(idArg);
        foreach (var o in new Option[] { titleOpt, textOpt, sourceOpt, activeOpt })
            sub.Options.Add(o);

        sub.SetAction(async (pr, ct) =>
        {
            var id = pr.GetValue(idArg)!;
            await using var ctx = await BrainContext.OpenAsync(ct);
            var current = await ctx.Store.GetNoteAsync(id, ct);
            if (current is null) { Console.Error.WriteLine($"error: note {id} not found"); return 1; }

            var newText = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);
            var patched = current with
            {
                Title = pr.GetValue(titleOpt) ?? current.Title,
                Content = newText ?? current.Content,
                Source = pr.GetValue(sourceOpt) ?? current.Source,
                Active = pr.GetValue(activeOpt) ?? current.Active,
            };

            if (!await ctx.Store.UpdateNoteAsync(patched, ct))
            {
                Console.Error.WriteLine($"error: update failed for note {id}");
                return 1;
            }
            await ctx.Embeddings.IndexNoteAsync(patched, ct);
            Console.WriteLine(patched.Id);
            return 0;
        });

        return sub;
    }
}
