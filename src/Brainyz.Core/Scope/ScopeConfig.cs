// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Scope;

/// <summary>
/// Reads the <c>[projects.paths]</c> section of the global brainyz config file,
/// which maps filesystem paths to project ids. Used as step 3 of the scope
/// resolution chain (D-010).
/// </summary>
/// <remarks>
/// Format:
/// <code>
/// [projects.paths]
/// "/home/me/work/ailang"      = "01JABC..."
/// "C:\\Users\\me\\payments"   = "01JXYZ..."
/// </code>
/// </remarks>
public static class ScopeConfig
{
    public sealed record PathMapping(string Path, string ProjectId);

    /// <summary>
    /// Reads and parses the config file. Returns an empty list when the file
    /// does not exist or contains no <c>[projects.paths]</c> section.
    /// </summary>
    public static IReadOnlyList<PathMapping> LoadPathMappings(string configFilePath)
    {
        if (!File.Exists(configFilePath)) return [];
        return ParsePathMappings(File.ReadAllText(configFilePath));
    }

    public static IReadOnlyList<PathMapping> ParsePathMappings(string tomlText)
    {
        var result = new List<PathMapping>();
        bool inSection = false;

        foreach (var rawLine in tomlText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('['))
            {
                inSection = line.Equals("[projects.paths]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = StripQuotes(line[..eq].Trim());
            var value = StripQuotes(line[(eq + 1)..].Trim());
            if (key.Length == 0 || value.Length == 0) continue;

            result.Add(new PathMapping(key, value));
        }

        return result;
    }

    /// <summary>
    /// Picks the mapping whose path is the longest ancestor of <paramref name="cwd"/>,
    /// or null when none matches. Comparison is case-insensitive on Windows and
    /// case-sensitive elsewhere, mirroring the platform's filesystem behaviour.
    /// </summary>
    public static PathMapping? MatchCwd(IReadOnlyList<PathMapping> mappings, string cwd)
    {
        if (mappings.Count == 0) return null;

        var normalizedCwd = NormalizePath(cwd);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        PathMapping? best = null;
        int bestLen = -1;
        foreach (var m in mappings)
        {
            var normalizedPath = NormalizePath(m.Path);
            if (!IsAncestor(normalizedPath, normalizedCwd, comparison)) continue;
            if (normalizedPath.Length > bestLen)
            {
                best = m;
                bestLen = normalizedPath.Length;
            }
        }
        return best;
    }

    private static string NormalizePath(string p)
    {
        var full = Path.GetFullPath(p);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsAncestor(string ancestor, string descendant, StringComparison cmp)
    {
        if (descendant.Equals(ancestor, cmp)) return true;
        if (!descendant.StartsWith(ancestor, cmp)) return false;
        var next = descendant[ancestor.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        return s;
    }
}
