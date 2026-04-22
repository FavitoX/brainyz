// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Scope;

/// <summary>
/// Reads the <c>.brain</c> marker file used as an explicit, checked-in scope
/// override (D-010 #1). File format: plain <c>key = value</c> lines, with
/// <c>#</c> for comments. Supported keys today: <c>project_id</c>, <c>slug</c>.
/// </summary>
public static class BrainFile
{
    public const string FileName = ".brain";

    public sealed record Marker(string? ProjectId, string? Slug, string Path);

    /// <summary>
    /// Walks from <paramref name="startDir"/> upwards and returns the first
    /// <c>.brain</c> file encountered. Null if none found up to the filesystem root.
    /// </summary>
    public static Marker? FindFromAncestors(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return Parse(File.ReadAllText(candidate), candidate);
            dir = dir.Parent;
        }
        return null;
    }

    public static Marker Parse(string text, string sourcePath)
    {
        string? projectId = null;
        string? slug = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim().Trim('"');
            if (value.Length == 0) continue;

            switch (key)
            {
                case "project_id": projectId = value; break;
                case "slug": slug = value; break;
            }
        }

        return new Marker(projectId, slug, sourcePath);
    }
}
