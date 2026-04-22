// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class PrinciplesTests : StoreTestBase
{
    [Fact]
    public async Task AddPrinciple_roundtrip()
    {
        var p = new Principle(
            Id: Ids.NewUlid(),
            ProjectId: null,
            Title: "Operational simplicity",
            Statement: "One file, one process, zero services",
            Rationale: "Backup is a cp; recovery is a restore of one file");

        await Store.AddPrincipleAsync(p);
        var found = await Store.GetPrincipleAsync(p.Id);

        Assert.NotNull(found);
        Assert.Equal("Operational simplicity", found!.Title);
        Assert.Equal("One file, one process, zero services", found.Statement);
        Assert.True(found.Active);
        Assert.Null(found.ProjectId);
    }

    [Fact]
    public async Task ListPrinciples_with_null_project_returns_only_global()
    {
        var project = new Project(Ids.NewUlid(), "ailang", "AILang");
        await Store.AddProjectAsync(project);

        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null, "Global 1", "g1"));
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null, "Global 2", "g2"));
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), project.Id, "Scoped", "s1"));

        var globals = await Store.ListPrinciplesAsync(projectId: null);
        Assert.Equal(2, globals.Count);
        Assert.All(globals, p => Assert.Null(p.ProjectId));

        var scoped = await Store.ListPrinciplesAsync(projectId: project.Id);
        Assert.Single(scoped);
        Assert.Equal("Scoped", scoped[0].Title);
    }

    [Fact]
    public async Task ListAllPrinciples_returns_rows_across_scopes()
    {
        var project = new Project(Ids.NewUlid(), "ailang", "AILang");
        await Store.AddProjectAsync(project);

        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null, "Global", "g"));
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), project.Id, "Scoped", "s"));

        var all = await Store.ListAllPrinciplesAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ListPrinciples_with_activeOnly_false_includes_archived()
    {
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null, "Live", "live"));
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null, "Archived", "arch", Active: false));

        var activeOnly = await Store.ListPrinciplesAsync(projectId: null, activeOnly: true);
        var everything = await Store.ListPrinciplesAsync(projectId: null, activeOnly: false);

        Assert.Single(activeOnly);
        Assert.Equal(2, everything.Count);
    }

    [Fact]
    public async Task SearchPrinciples_matches_statement_text_via_FTS5()
    {
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null,
            "Auth mandatory", "Every handler must enforce authentication"));
        await Store.AddPrincipleAsync(new Principle(Ids.NewUlid(), null,
            "No SQL concat", "Use parameterized queries everywhere"));

        var hits = await Store.SearchPrinciplesAsync("authentication");

        Assert.Single(hits);
        Assert.Equal("Auth mandatory", hits[0].Title);
    }
}
