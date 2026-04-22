// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Scope;

/// <summary>
/// Reads the remote origin URL from a repository's <c>.git/config</c> without
/// shelling out to <c>git</c>. Handles the three common layouts:
/// <list type="bullet">
///   <item>Regular repo — <c>.git</c> is a directory containing <c>config</c>.</item>
///   <item>Worktree or submodule — <c>.git</c> is a file pointing at <c>gitdir: …</c>.</item>
///   <item>Bare repo — <c>config</c> sits directly in the given directory.</item>
/// </list>
/// </summary>
public static class GitConfig
{
    /// <summary>
    /// Walks from <paramref name="startDir"/> upwards looking for a
    /// <c>.git</c> entry and returns the <c>origin</c> remote URL, if any.
    /// </summary>
    public static string? FindOriginUrlFromAncestors(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(dotGit))
                return ReadOriginUrl(Path.Combine(dotGit, "config"));
            if (File.Exists(dotGit))
            {
                var gitDir = ResolveGitdirPointer(dotGit, dir.FullName);
                if (gitDir is not null)
                    return ReadOriginUrl(Path.Combine(gitDir, "config"));
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ResolveGitdirPointer(string dotGitFile, string repoRoot)
    {
        // .git file format: `gitdir: <path>` (relative or absolute)
        var content = File.ReadAllText(dotGitFile).Trim();
        const string prefix = "gitdir:";
        if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var pointer = content[prefix.Length..].Trim();
        return Path.IsPathRooted(pointer) ? pointer : Path.GetFullPath(Path.Combine(repoRoot, pointer));
    }

    /// <summary>
    /// Parses an INI-like git config file and returns the <c>url</c> value
    /// inside the <c>[remote "origin"]</c> section. Null if the file is
    /// missing, unreadable, or lacks an origin.
    /// </summary>
    public static string? ReadOriginUrl(string configPath)
    {
        if (!File.Exists(configPath)) return null;

        bool inOrigin = false;
        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inOrigin = IsOriginHeader(line);
                continue;
            }

            if (!inOrigin) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (!key.Equals("url", StringComparison.OrdinalIgnoreCase)) continue;

            return line[(eq + 1)..].Trim();
        }

        return null;
    }

    // Accepts both classic [remote "origin"] and subsection-less [remote.origin].
    private static bool IsOriginHeader(string sectionLine)
    {
        var inner = sectionLine[1..^1].Trim();
        if (inner.StartsWith("remote \"origin\"", StringComparison.OrdinalIgnoreCase)) return true;
        if (inner.Equals("remote.origin", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
