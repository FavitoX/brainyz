// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class ProjectsTests : StoreTestBase
{
    [Fact]
    public async Task AddProject_and_GetProjectBySlug_roundtrip()
    {
        var project = new Project(
            Id: Ids.NewUlid(),
            Slug: "brainyz",
            Name: "brainyz",
            Description: "Personal global knowledge layer");

        await Store.AddProjectAsync(project);
        var found = await Store.GetProjectBySlugAsync("brainyz");

        Assert.NotNull(found);
        Assert.Equal(project.Id, found!.Id);
        Assert.Equal("brainyz", found.Slug);
        Assert.Equal("Personal global knowledge layer", found.Description);
        Assert.True(found.CreatedAtMs > 0);
        Assert.True(found.UpdatedAtMs > 0);
    }

    [Fact]
    public async Task GetProject_by_id_returns_the_same_row()
    {
        var project = new Project(Ids.NewUlid(), "ailang", "AILang");
        await Store.AddProjectAsync(project);

        var found = await Store.GetProjectAsync(project.Id);

        Assert.NotNull(found);
        Assert.Equal("ailang", found!.Slug);
    }

    [Fact]
    public async Task ListProjects_returns_rows_sorted_by_slug()
    {
        await Store.AddProjectAsync(new Project(Ids.NewUlid(), "zulu", "Zulu"));
        await Store.AddProjectAsync(new Project(Ids.NewUlid(), "alpha", "Alpha"));
        await Store.AddProjectAsync(new Project(Ids.NewUlid(), "mike", "Mike"));

        var all = await Store.ListProjectsAsync();

        Assert.Equal(new[] { "alpha", "mike", "zulu" }, all.Select(p => p.Slug));
    }
}
