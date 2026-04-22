// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz add (decision|principle|note) …</c> — create a new entity.
/// The body text is taken from <c>--text</c> or, if omitted, from stdin when
/// stdin is redirected.
/// </summary>
public static class AddCommand
{
    public static Command Build()
    {
        var cmd = new Command("add", "Add a decision, principle or note");
        cmd.Subcommands.Add(BuildDecision());
        cmd.Subcommands.Add(BuildPrinciple());
        cmd.Subcommands.Add(BuildNote());
        return cmd;
    }

    // ───────── add decision ─────────

    private static Command BuildDecision()
    {
        var sub = new Command("decision", "Add a decision");

        var titleArg = new Argument<string>("title") { Description = "Short title of the decision" };
        var textOpt = new Option<string?>("--text") { Description = "Body text of the decision (or pipe via stdin)" };
        var contextOpt = new Option<string?>("--context") { Description = "Problem / situation that led to the decision" };
        var rationaleOpt = new Option<string?>("--rationale") { Description = "Why this option was chosen" };
        var consequencesOpt = new Option<string?>("--consequences") { Description = "What this implies going forward" };
        var confidenceOpt = new Option<string?>("--confidence") { Description = "low | medium | high" };
        var statusOpt = new Option<string?>("--status") { Description = "accepted (default) | proposed | deprecated | superseded" };
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Override the resolved project scope" };

        sub.Arguments.Add(titleArg);
        foreach (var o in new Option[] { textOpt, contextOpt, rationaleOpt, consequencesOpt, confidenceOpt, statusOpt, projectOpt })
            sub.Options.Add(o);

        sub.SetAction(async (pr, ct) =>
        {
            var text = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);
            if (text is null)
            {
                Console.Error.WriteLine("error: decision body required. Pass --text \"…\" or pipe via stdin.");
                return 2;
            }

            await using var ctx = await BrainContext.OpenAsync(ct);
            var (projectId, err) = await Scoping.ResolveForWriteAsync(
                ctx, pr.GetValue(projectOpt), Directory.GetCurrentDirectory(), ct);
            if (err is not null) { Console.Error.WriteLine($"error: {err}"); return 1; }

            Confidence? conf = ParseConfidence(pr.GetValue(confidenceOpt), out var confErr);
            if (confErr is not null) { Console.Error.WriteLine($"error: {confErr}"); return 2; }

            DecisionStatus status = ParseStatus(pr.GetValue(statusOpt), out var stErr);
            if (stErr is not null) { Console.Error.WriteLine($"error: {stErr}"); return 2; }

            var decision = new Decision(
                Id: Ids.NewUlid(),
                ProjectId: projectId,
                Title: pr.GetValue(titleArg)!,
                Text: text,
                Context: pr.GetValue(contextOpt),
                Rationale: pr.GetValue(rationaleOpt),
                Consequences: pr.GetValue(consequencesOpt),
                Status: status,
                Confidence: conf);

            await ctx.Store.AddDecisionAsync(decision, ct: ct);
            await MaybeIndex(ctx, () => ctx.Embeddings.IndexDecisionAsync(decision, ct));
            Console.WriteLine(decision.Id);
            return 0;
        });

        return sub;
    }

    // ───────── add principle ─────────

    private static Command BuildPrinciple()
    {
        var sub = new Command("principle", "Add a principle");

        var titleArg = new Argument<string>("title") { Description = "Short title of the principle" };
        var textOpt = new Option<string?>("--text") { Description = "The principle statement (or pipe via stdin)" };
        var rationaleOpt = new Option<string?>("--rationale") { Description = "Why this principle applies" };
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Override the resolved project scope" };

        sub.Arguments.Add(titleArg);
        sub.Options.Add(textOpt);
        sub.Options.Add(rationaleOpt);
        sub.Options.Add(projectOpt);

        sub.SetAction(async (pr, ct) =>
        {
            var statement = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);
            if (statement is null)
            {
                Console.Error.WriteLine("error: principle statement required. Pass --text \"…\" or pipe via stdin.");
                return 2;
            }

            await using var ctx = await BrainContext.OpenAsync(ct);
            var (projectId, err) = await Scoping.ResolveForWriteAsync(
                ctx, pr.GetValue(projectOpt), Directory.GetCurrentDirectory(), ct);
            if (err is not null) { Console.Error.WriteLine($"error: {err}"); return 1; }

            var principle = new Principle(
                Id: Ids.NewUlid(),
                ProjectId: projectId,
                Title: pr.GetValue(titleArg)!,
                Statement: statement,
                Rationale: pr.GetValue(rationaleOpt));

            await ctx.Store.AddPrincipleAsync(principle, ct);
            await MaybeIndex(ctx, () => ctx.Embeddings.IndexPrincipleAsync(principle, ct));
            Console.WriteLine(principle.Id);
            return 0;
        });

        return sub;
    }

    // ───────── add note ─────────

    private static Command BuildNote()
    {
        var sub = new Command("note", "Add a reference note");

        var titleArg = new Argument<string>("title") { Description = "Short title of the note" };
        var textOpt = new Option<string?>("--text") { Description = "Body content of the note (or pipe via stdin)" };
        var sourceOpt = new Option<string?>("--source") { Description = "URL or origin reference" };
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Override the resolved project scope" };

        sub.Arguments.Add(titleArg);
        sub.Options.Add(textOpt);
        sub.Options.Add(sourceOpt);
        sub.Options.Add(projectOpt);

        sub.SetAction(async (pr, ct) =>
        {
            var body = await InputText.ResolveAsync(pr.GetValue(textOpt), ct);
            if (body is null)
            {
                Console.Error.WriteLine("error: note body required. Pass --text \"…\" or pipe via stdin.");
                return 2;
            }

            await using var ctx = await BrainContext.OpenAsync(ct);
            var (projectId, err) = await Scoping.ResolveForWriteAsync(
                ctx, pr.GetValue(projectOpt), Directory.GetCurrentDirectory(), ct);
            if (err is not null) { Console.Error.WriteLine($"error: {err}"); return 1; }

            var note = new Note(
                Id: Ids.NewUlid(),
                ProjectId: projectId,
                Title: pr.GetValue(titleArg)!,
                Content: body,
                Source: pr.GetValue(sourceOpt));

            await ctx.Store.AddNoteAsync(note, ct);
            await MaybeIndex(ctx, () => ctx.Embeddings.IndexNoteAsync(note, ct));
            Console.WriteLine(note.Id);
            return 0;
        });

        return sub;
    }

    // Best-effort indexing — a missing Ollama must never block a write.
    private static async Task MaybeIndex(BrainContext ctx, Func<Task<Brainyz.Core.Embeddings.IndexResult>> index)
    {
        var result = await index();
        if (result == Brainyz.Core.Embeddings.IndexResult.ProviderUnavailable)
            Console.Error.WriteLine(
                $"note: Ollama unreachable at {ctx.EmbeddingConfig.Host} — entry saved, but not embedded. " +
                "Run `brainz reindex` once Ollama is up.");
    }

    // ───────── parsers ─────────

    private static Confidence? ParseConfidence(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(raw)) return null;
        switch (raw.ToLowerInvariant())
        {
            case "low": return Confidence.Low;
            case "medium": return Confidence.Medium;
            case "high": return Confidence.High;
            default:
                error = $"invalid --confidence '{raw}' (expected: low | medium | high)";
                return null;
        }
    }

    private static DecisionStatus ParseStatus(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(raw)) return DecisionStatus.Accepted;
        switch (raw.ToLowerInvariant())
        {
            case "proposed": return DecisionStatus.Proposed;
            case "accepted": return DecisionStatus.Accepted;
            case "deprecated": return DecisionStatus.Deprecated;
            case "superseded": return DecisionStatus.Superseded;
            default:
                error = $"invalid --status '{raw}' (expected: proposed | accepted | deprecated | superseded)";
                return DecisionStatus.Accepted;
        }
    }
}
