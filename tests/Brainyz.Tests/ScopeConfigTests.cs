// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Scope;

namespace Brainyz.Tests;

public sealed class ScopeConfigTests
{
    [Fact]
    public void ParsePathMappings_reads_quoted_keys_and_values_inside_section()
    {
        var text = """
            # global config
            [other.section]
            foo = "bar"

            [projects.paths]
            "/home/me/work/ailang" = "01JAILANG"
            "/home/me/work/payments" = "01JPAYMENTS"
            """;

        var mappings = ScopeConfig.ParsePathMappings(text);

        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.Path == "/home/me/work/ailang"   && m.ProjectId == "01JAILANG");
        Assert.Contains(mappings, m => m.Path == "/home/me/work/payments" && m.ProjectId == "01JPAYMENTS");
    }

    [Fact]
    public void ParsePathMappings_ignores_keys_outside_projects_paths_section()
    {
        var text = """
            [other]
            "/tmp" = "shouldIgnore"

            [projects.paths]
            "/real" = "01JREAL"
            """;

        var mappings = ScopeConfig.ParsePathMappings(text);

        Assert.Single(mappings);
        Assert.Equal("/real", mappings[0].Path);
    }

    [Fact]
    public void MatchCwd_returns_mapping_when_path_is_ancestor_of_cwd()
    {
        var root = Path.GetTempPath();
        var mappings = new List<ScopeConfig.PathMapping>
        {
            new(Path.Combine(root, "ailang"), "01JAILANG"),
        };

        var match = ScopeConfig.MatchCwd(mappings, Path.Combine(root, "ailang", "src", "deep"));

        Assert.NotNull(match);
        Assert.Equal("01JAILANG", match!.ProjectId);
    }

    [Fact]
    public void MatchCwd_picks_the_longest_matching_ancestor()
    {
        var root = Path.GetTempPath();
        var mappings = new List<ScopeConfig.PathMapping>
        {
            new(Path.Combine(root, "work"), "01JOUTER"),
            new(Path.Combine(root, "work", "ailang"), "01JAILANG"),
        };

        var match = ScopeConfig.MatchCwd(mappings, Path.Combine(root, "work", "ailang", "src"));

        Assert.Equal("01JAILANG", match!.ProjectId);
    }

    [Fact]
    public void MatchCwd_returns_null_when_no_mapping_is_an_ancestor()
    {
        var mappings = new List<ScopeConfig.PathMapping>
        {
            new(Path.Combine(Path.GetTempPath(), "ailang"), "01JAILANG"),
        };

        var match = ScopeConfig.MatchCwd(mappings, Path.Combine(Path.GetTempPath(), "payments"));

        Assert.Null(match);
    }
}
