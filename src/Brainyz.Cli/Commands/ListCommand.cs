// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli.Output;
using Brainyz.Core.Scope;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz list (decisions|principles|notes|projects)</c> — list rows
/// scoped to the current project by default. Use <c>--global</c> to list
/// only globals, or <c>--all</c> to ignore scoping.
/// </summary>
public static class ListCommand
{
    public static Command Build()
    {
        var cmd = new Command("list", "List decisions, principles, notes or projects");
        cmd.Subcommands.Add(BuildDecisions());
        cmd.Subcommands.Add(BuildPrinciples());
        cmd.Subcommands.Add(BuildNotes());
        cmd.Subcommands.Add(BuildProjects());
        return cmd;
    }

    private static Command BuildDecisions()
    {
        var sub = new Command("decisions", "List decisions in the resolved scope");
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Explicit project slug (overrides scope resolution)" };
        var limitOpt = new Option<int>("--limit") { Description = "Max rows to return (default: 50)", DefaultValueFactory = _ => 50 };
        var globalOpt = new Option<bool>("--global") { Description = "List only global decisions" };
        var allOpt = new Option<bool>("--all") { Description = "List across every project" };
        sub.Options.Add(projectOpt);
        sub.Options.Add(limitOpt);
        sub.Options.Add(globalOpt);
        sub.Options.Add(allOpt);

        sub.SetAction(async (pr, ct) =>
        {
            await using var ctx = await BrainContext.OpenAsync(ct);
            var limit = pr.GetValue(limitOpt);
            if (pr.GetValue(allOpt))
            {
                foreach (var d in await ctx.Store.ListAllDecisionsAsync(limit, ct))
                    Render.DecisionRow(d);
                return 0;
            }
            var (projectId, includeGlobal) = await ResolveScope(ctx, pr, projectOpt, globalOpt, ct);
            foreach (var d in await ctx.Store.ListDecisionsAsync(projectId, includeGlobal, limit, ct))
                Render.DecisionRow(d);
            return 0;
        });
        return sub;
    }

    private static Command BuildPrinciples()
    {
        var sub = new Command("principles", "List principles in the resolved scope");
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Explicit project slug" };
        var activeOnlyOpt = new Option<bool>("--active-only") { Description = "Hide archived principles", DefaultValueFactory = _ => true };
        var globalOpt = new Option<bool>("--global") { Description = "List only global principles" };
        var allOpt = new Option<bool>("--all") { Description = "List across every project" };
        sub.Options.Add(projectOpt);
        sub.Options.Add(activeOnlyOpt);
        sub.Options.Add(globalOpt);
        sub.Options.Add(allOpt);

        sub.SetAction(async (pr, ct) =>
        {
            await using var ctx = await BrainContext.OpenAsync(ct);
            var activeOnly = pr.GetValue(activeOnlyOpt);

            if (pr.GetValue(allOpt))
            {
                foreach (var p in await ctx.Store.ListAllPrinciplesAsync(activeOnly, ct))
                    Render.PrincipleRow(p);
                return 0;
            }

            var (projectId, _) = await ResolveScope(ctx, pr, projectOpt, globalOpt, ct);
            foreach (var p in await ctx.Store.ListPrinciplesAsync(projectId, activeOnly, ct))
                Render.PrincipleRow(p);
            return 0;
        });
        return sub;
    }

    private static Command BuildNotes()
    {
        var sub = new Command("notes", "List notes in the resolved scope");
        var projectOpt = new Option<string?>("--project", "-p") { Description = "Explicit project slug" };
        var includeArchivedOpt = new Option<bool>("--include-archived") { Description = "Include archived notes" };
        var limitOpt = new Option<int>("--limit") { Description = "Max rows to return (default: 50)", DefaultValueFactory = _ => 50 };
        var globalOpt = new Option<bool>("--global") { Description = "List only global notes" };
        var allOpt = new Option<bool>("--all") { Description = "List across every project" };
        sub.Options.Add(projectOpt);
        sub.Options.Add(includeArchivedOpt);
        sub.Options.Add(limitOpt);
        sub.Options.Add(globalOpt);
        sub.Options.Add(allOpt);

        sub.SetAction(async (pr, ct) =>
        {
            await using var ctx = await BrainContext.OpenAsync(ct);
            var activeOnly = !pr.GetValue(includeArchivedOpt);
            var limit = pr.GetValue(limitOpt);
            if (pr.GetValue(allOpt))
            {
                foreach (var n in await ctx.Store.ListAllNotesAsync(activeOnly, limit, ct))
                    Render.NoteRow(n);
                return 0;
            }
            var (projectId, includeGlobal) = await ResolveScope(ctx, pr, projectOpt, globalOpt, ct);
            foreach (var n in await ctx.Store.ListNotesAsync(projectId, includeGlobal, activeOnly, limit, ct))
                Render.NoteRow(n);
            return 0;
        });
        return sub;
    }

    private static Command BuildProjects()
    {
        var sub = new Command("projects", "List registered projects");
        sub.SetAction(async (pr, ct) =>
        {
            await using var ctx = await BrainContext.OpenAsync(ct);
            foreach (var p in await ctx.Store.ListProjectsAsync(ct))
                Render.ProjectRow(p);
            return 0;
        });
        return sub;
    }

    // Returns (projectId, includeGlobal) — ready to feed into the scoped
    // ListDecisionsAsync / ListPrinciplesAsync / ListNotesAsync overloads.
    // `--all` is handled by the individual subcommands before reaching here.
    private static async Task<(string? projectId, bool includeGlobal)> ResolveScope(
        BrainContext ctx,
        System.CommandLine.ParseResult pr,
        Option<string?> projectOpt,
        Option<bool> globalOpt,
        CancellationToken ct)
    {
        if (pr.GetValue(globalOpt)) return (null, true);

        var explicitSlug = pr.GetValue(projectOpt);
        if (!string.IsNullOrEmpty(explicitSlug))
        {
            var project = await ctx.Store.GetProjectBySlugAsync(explicitSlug, ct);
            return (project?.Id, includeGlobal: true);
        }

        var scope = await ctx.Resolver.ResolveAsync(Directory.GetCurrentDirectory(), ct);
        return (scope.ProjectId, includeGlobal: true);
    }
}
