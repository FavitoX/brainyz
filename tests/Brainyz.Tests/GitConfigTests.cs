// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Scope;

namespace Brainyz.Tests;

public sealed class GitConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"brainyz-gitcfg-{Guid.NewGuid():N}");

    public GitConfigTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void ReadOriginUrl_returns_url_from_remote_origin_section()
    {
        var configPath = Path.Combine(_root, "config");
        File.WriteAllText(configPath, """
            [core]
                repositoryformatversion = 0
            [remote "origin"]
                url = git@github.com:favitox/brainyz.git
                fetch = +refs/heads/*:refs/remotes/origin/*
            """);

        var url = GitConfig.ReadOriginUrl(configPath);

        Assert.Equal("git@github.com:favitox/brainyz.git", url);
    }

    [Fact]
    public void ReadOriginUrl_ignores_other_remote_sections()
    {
        var configPath = Path.Combine(_root, "config");
        File.WriteAllText(configPath, """
            [remote "upstream"]
                url = https://github.com/other/brainyz.git
            [remote "origin"]
                url = git@github.com:me/brainyz.git
            """);

        Assert.Equal("git@github.com:me/brainyz.git", GitConfig.ReadOriginUrl(configPath));
    }

    [Fact]
    public void ReadOriginUrl_returns_null_when_there_is_no_origin()
    {
        var configPath = Path.Combine(_root, "config");
        File.WriteAllText(configPath, """
            [core]
                repositoryformatversion = 0
            """);

        Assert.Null(GitConfig.ReadOriginUrl(configPath));
    }

    [Fact]
    public void FindOriginUrlFromAncestors_walks_up_to_find_dot_git_dir()
    {
        var dotGit = Path.Combine(_root, ".git");
        Directory.CreateDirectory(dotGit);
        File.WriteAllText(Path.Combine(dotGit, "config"), """
            [remote "origin"]
                url = https://example.com/repo.git
            """);
        var nested = Path.Combine(_root, "src", "nested");
        Directory.CreateDirectory(nested);

        var url = GitConfig.FindOriginUrlFromAncestors(nested);

        Assert.Equal("https://example.com/repo.git", url);
    }

    [Fact]
    public void FindOriginUrlFromAncestors_handles_dot_git_file_pointer()
    {
        // Worktree / submodule layout: `.git` is a file pointing to the actual gitdir.
        var gitDir = Path.Combine(_root, "actual-gitdir");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), """
            [remote "origin"]
                url = git@github.com:ext/worktree.git
            """);

        var repoRoot = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repoRoot);
        File.WriteAllText(Path.Combine(repoRoot, ".git"), $"gitdir: {gitDir}\n");

        var url = GitConfig.FindOriginUrlFromAncestors(repoRoot);

        Assert.Equal("git@github.com:ext/worktree.git", url);
    }

    [Fact]
    public void FindOriginUrlFromAncestors_returns_null_outside_any_git_repo()
    {
        Assert.Null(GitConfig.FindOriginUrlFromAncestors(_root));
    }
}
