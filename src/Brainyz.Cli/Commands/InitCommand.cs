// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Models;
using Brainyz.Core.Scope;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz init</c> — initialize the brainyz store and optionally register
/// the current working directory as a project.
/// </summary>
public static class InitCommand
{
    public static Command Build()
    {
        var cmd = new Command("init", "Initialize brainyz and optionally register this repo as a project");

        var projectOpt = new Option<string?>("--project", "-p")
        {
            Description = "Project slug to register for the current directory"
        };
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Human-readable project name (defaults to the slug)"
        };

        cmd.Options.Add(projectOpt);
        cmd.Options.Add(nameOpt);

        cmd.SetAction(async (pr, ct) => await RunAsync(
            pr.GetValue(projectOpt),
            pr.GetValue(nameOpt),
            ct));

        return cmd;
    }

    private static async Task<int> RunAsync(string? slug, string? name, CancellationToken ct)
    {
        await using var ctx = await BrainContext.OpenAsync(ct);
        Console.WriteLine($"brainyz ready at {ctx.Paths.DbPath}");

        if (string.IsNullOrEmpty(slug))
        {
            Console.WriteLine("no project registered. Run `brainz init --project <slug>` to register one.");
            return 0;
        }

        var existing = await ctx.Store.GetProjectBySlugAsync(slug, ct);
        Project project;
        if (existing is not null)
        {
            project = existing;
            Console.WriteLine($"project '{slug}' already registered ({project.Id}).");
        }
        else
        {
            project = new Project(
                Id: Ids.NewUlid(),
                Slug: slug,
                Name: name ?? slug);
            await ctx.Store.AddProjectAsync(project, ct);
            Console.WriteLine($"project '{slug}' registered ({project.Id}).");
        }

        // Register git origin (idempotent on remote_url — if the URL is
        // already mapped to another project, that mapping wins by UNIQUE
        // constraint and we surface a warning).
        var cwd = Directory.GetCurrentDirectory();
        var originUrl = GitConfig.FindOriginUrlFromAncestors(cwd);
        if (originUrl is not null)
        {
            var existingForUrl = await ctx.Store.FindProjectByRemoteUrlAsync(originUrl, ct);
            if (existingForUrl is null)
            {
                await ctx.Store.AddProjectRemoteAsync(new ProjectRemote(
                    Id: Ids.NewUlid(),
                    ProjectId: project.Id,
                    RemoteUrl: originUrl,
                    Role: RemoteRole.Origin), ct);
                Console.WriteLine($"git origin registered: {originUrl}");
            }
            else if (existingForUrl.Id != project.Id)
            {
                Console.WriteLine($"warning: git origin {originUrl} is already mapped to project '{existingForUrl.Slug}'.");
            }
        }

        // Drop a .brain marker in cwd so the resolver finds the project
        // deterministically even when the git remote mapping breaks.
        var brainPath = Path.Combine(cwd, ".brain");
        if (!File.Exists(brainPath))
        {
            await File.WriteAllTextAsync(brainPath, $"project_id = {project.Id}\n", ct);
            Console.WriteLine($"wrote {brainPath}");
        }

        return 0;
    }
}
