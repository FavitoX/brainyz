// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class RemotesTests : StoreTestBase
{
    [Fact]
    public async Task AddProjectRemote_and_FindProjectByRemoteUrl_roundtrip()
    {
        var project = new Project(Ids.NewUlid(), "brainyz", "brainyz");
        await Store.AddProjectAsync(project);

        await Store.AddProjectRemoteAsync(new ProjectRemote(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            RemoteUrl: "git@github.com:favitox/brainyz.git",
            Role: RemoteRole.Origin));

        var found = await Store.FindProjectByRemoteUrlAsync("git@github.com:favitox/brainyz.git");
        Assert.NotNull(found);
        Assert.Equal(project.Id, found!.Id);
    }

    [Fact]
    public async Task ListRemotesForProject_returns_all_registered_remotes()
    {
        var project = new Project(Ids.NewUlid(), "brainyz", "brainyz");
        await Store.AddProjectAsync(project);

        await Store.AddProjectRemoteAsync(new ProjectRemote(
            Ids.NewUlid(), project.Id,
            "git@github.com:favitox/brainyz.git", RemoteRole.Origin));
        await Store.AddProjectRemoteAsync(new ProjectRemote(
            Ids.NewUlid(), project.Id,
            "https://gitlab.example.com/mirror/brainyz.git", RemoteRole.Mirror));

        var remotes = await Store.ListRemotesForProjectAsync(project.Id);

        Assert.Equal(2, remotes.Count);
        Assert.Contains(remotes, r => r.Role == RemoteRole.Origin);
        Assert.Contains(remotes, r => r.Role == RemoteRole.Mirror);
    }

    [Fact]
    public async Task FindProjectByRemoteUrl_returns_null_when_no_match()
    {
        var project = new Project(Ids.NewUlid(), "brainyz", "brainyz");
        await Store.AddProjectAsync(project);

        var found = await Store.FindProjectByRemoteUrlAsync("https://nowhere.example/repo.git");

        Assert.Null(found);
    }
}
