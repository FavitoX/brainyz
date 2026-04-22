// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Scope;

namespace Brainyz.Tests;

public sealed class BrainFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"brainyz-brainfile-{Guid.NewGuid():N}");

    public BrainFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Parse_reads_project_id_and_slug()
    {
        var marker = BrainFile.Parse(
            """
            # sample brain file
            project_id = 01JABCDEFGHIJKLMNOPQRSTUVW
            slug = ailang
            """,
            sourcePath: "/fake/.brain");

        Assert.Equal("01JABCDEFGHIJKLMNOPQRSTUVW", marker.ProjectId);
        Assert.Equal("ailang", marker.Slug);
        Assert.Equal("/fake/.brain", marker.Path);
    }

    [Fact]
    public void Parse_ignores_comments_and_blank_lines_and_quotes()
    {
        var marker = BrainFile.Parse(
            """
            # top comment

            slug = "ailang"
            """,
            sourcePath: "/fake/.brain");

        Assert.Equal("ailang", marker.Slug);
        Assert.Null(marker.ProjectId);
    }

    [Fact]
    public void FindFromAncestors_returns_null_when_no_brain_file_exists()
    {
        var marker = BrainFile.FindFromAncestors(_root);
        Assert.Null(marker);
    }

    [Fact]
    public void FindFromAncestors_walks_up_from_nested_dir_to_find_marker()
    {
        File.WriteAllText(Path.Combine(_root, BrainFile.FileName), "project_id = 01J");
        var nested = Path.Combine(_root, "a", "b", "c");
        Directory.CreateDirectory(nested);

        var marker = BrainFile.FindFromAncestors(nested);

        Assert.NotNull(marker);
        Assert.Equal("01J", marker!.ProjectId);
    }

    [Fact]
    public void FindFromAncestors_prefers_the_closest_marker_on_the_way_up()
    {
        File.WriteAllText(Path.Combine(_root, BrainFile.FileName), "slug = outer");
        var nested = Path.Combine(_root, "inner");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, BrainFile.FileName), "slug = inner");

        var marker = BrainFile.FindFromAncestors(nested);

        Assert.Equal("inner", marker!.Slug);
    }
}
