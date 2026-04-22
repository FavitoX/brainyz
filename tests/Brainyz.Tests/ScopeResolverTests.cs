// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;
using Brainyz.Core.Scope;

namespace Brainyz.Tests;

public sealed class ScopeResolverTests : StoreTestBase
{
    private string _workTree = null!;
    private BrainyzPaths _paths = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _workTree = Path.Combine(Path.GetTempPath(), $"brainyz-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workTree);
        _paths = new BrainyzPaths(Path.Combine(_workTree, "_cfg"));
        Directory.CreateDirectory(_paths.ConfigDir);
    }

    public override async Task DisposeAsync()
    {
        try { Directory.Delete(_workTree, recursive: true); } catch { /* best effort */ }
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Step1_brain_file_with_project_id_wins()
    {
        var project = new Project(Ids.NewUlid(), "ailang", "AILang");
        await Store.AddProjectAsync(project);
        File.WriteAllText(Path.Combine(_workTree, BrainFile.FileName),
            $"project_id = {project.Id}");

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.BrainFile, result.Source);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal("ailang", result.ProjectSlug);
    }

    [Fact]
    public async Task Step1_brain_file_with_unknown_slug_falls_through_to_global()
    {
        File.WriteAllText(Path.Combine(_workTree, BrainFile.FileName),
            "slug = not-registered-yet");

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.Global, result.Source);
        Assert.Null(result.ProjectId);
    }

    [Fact]
    public async Task Step2_git_origin_matches_project_remotes_table()
    {
        var project = new Project(Ids.NewUlid(), "brainyz", "brainyz");
        await Store.AddProjectAsync(project);
        await Store.AddProjectRemoteAsync(new ProjectRemote(
            Ids.NewUlid(), project.Id,
            "git@github.com:favitox/brainyz.git", RemoteRole.Origin));

        var dotGit = Path.Combine(_workTree, ".git");
        Directory.CreateDirectory(dotGit);
        File.WriteAllText(Path.Combine(dotGit, "config"), """
            [remote "origin"]
                url = git@github.com:favitox/brainyz.git
            """);

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.GitRemote, result.Source);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal("git@github.com:favitox/brainyz.git", result.Hint);
    }

    [Fact]
    public async Task Step2_git_origin_that_is_not_registered_falls_through()
    {
        var dotGit = Path.Combine(_workTree, ".git");
        Directory.CreateDirectory(dotGit);
        File.WriteAllText(Path.Combine(dotGit, "config"), """
            [remote "origin"]
                url = https://example.com/unregistered.git
            """);

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.Global, result.Source);
    }

    [Fact]
    public async Task Step3_config_path_map_matches_ancestor_of_cwd()
    {
        var project = new Project(Ids.NewUlid(), "payments", "Payments");
        await Store.AddProjectAsync(project);

        // No .brain, no .git — only the path map should fire.
        File.WriteAllText(_paths.ConfigFile, $"""
            [projects.paths]
            "{_workTree.Replace("\\", "\\\\")}" = "{project.Id}"
            """);

        var nested = Path.Combine(_workTree, "src", "deep");
        Directory.CreateDirectory(nested);

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(nested);

        Assert.Equal(ScopeSource.ConfigPathMap, result.Source);
        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal("payments", result.ProjectSlug);
    }

    [Fact]
    public async Task Step4_global_fallback_when_nothing_matches()
    {
        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.Global, result.Source);
        Assert.Null(result.ProjectId);
    }

    [Fact]
    public async Task Precedence_brain_file_beats_git_remote_and_config_map()
    {
        // Register three distinct projects so we can prove which one wins.
        var pBrain   = new Project(Ids.NewUlid(), "from-brain",   "Brain");
        var pGit     = new Project(Ids.NewUlid(), "from-git",     "Git");
        var pConfig  = new Project(Ids.NewUlid(), "from-config",  "Config");
        await Store.AddProjectAsync(pBrain);
        await Store.AddProjectAsync(pGit);
        await Store.AddProjectAsync(pConfig);

        await Store.AddProjectRemoteAsync(new ProjectRemote(
            Ids.NewUlid(), pGit.Id, "https://example.com/from-git.git", RemoteRole.Origin));

        // .brain (step 1)
        File.WriteAllText(Path.Combine(_workTree, BrainFile.FileName),
            $"project_id = {pBrain.Id}");
        // .git/config (step 2)
        var dotGit = Path.Combine(_workTree, ".git");
        Directory.CreateDirectory(dotGit);
        File.WriteAllText(Path.Combine(dotGit, "config"), """
            [remote "origin"]
                url = https://example.com/from-git.git
            """);
        // config path map (step 3)
        File.WriteAllText(_paths.ConfigFile, $"""
            [projects.paths]
            "{_workTree.Replace("\\", "\\\\")}" = "{pConfig.Id}"
            """);

        var resolver = new ScopeResolver(Store, _paths);
        var result = await resolver.ResolveAsync(_workTree);

        Assert.Equal(ScopeSource.BrainFile, result.Source);
        Assert.Equal(pBrain.Id, result.ProjectId);
    }
}
