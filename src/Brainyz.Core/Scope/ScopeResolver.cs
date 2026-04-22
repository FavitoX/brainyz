// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Storage;

namespace Brainyz.Core.Scope;

/// <summary>
/// Resolves the current project scope from a working directory, following the
/// precedence chain defined in D-010:
/// <list type="number">
///   <item><c>.brain</c> file in <paramref name="cwd"/> or an ancestor.</item>
///   <item>Git <c>origin</c> remote matched against <c>project_remotes</c>.</item>
///   <item><c>[projects.paths]</c> mapping in <c>config.toml</c>.</item>
///   <item>Global fallback (no project filter).</item>
/// </list>
/// </summary>
public sealed class ScopeResolver(BrainStore store, BrainyzPaths paths)
{
    public async Task<ScopeResolution> ResolveAsync(string cwd, CancellationToken ct = default)
    {
        // 1. .brain file walk.
        var marker = BrainFile.FindFromAncestors(cwd);
        if (marker is not null)
        {
            var fromMarker = await ResolveFromMarkerAsync(marker, ct);
            if (fromMarker is not null) return fromMarker;
        }

        // 2. Git origin match.
        var originUrl = GitConfig.FindOriginUrlFromAncestors(cwd);
        if (originUrl is not null)
        {
            var project = await store.FindProjectByRemoteUrlAsync(originUrl, ct);
            if (project is not null)
                return new ScopeResolution(ScopeSource.GitRemote, project.Id, project.Slug, originUrl);
        }

        // 3. Config path map.
        var mappings = ScopeConfig.LoadPathMappings(paths.ConfigFile);
        var mapping = ScopeConfig.MatchCwd(mappings, cwd);
        if (mapping is not null)
        {
            var project = await store.GetProjectAsync(mapping.ProjectId, ct);
            return new ScopeResolution(
                Source: ScopeSource.ConfigPathMap,
                ProjectId: mapping.ProjectId,
                ProjectSlug: project?.Slug,
                Hint: mapping.Path);
        }

        // 4. Global fallback.
        return new ScopeResolution(ScopeSource.Global, null, null, null);
    }

    private async Task<ScopeResolution?> ResolveFromMarkerAsync(
        BrainFile.Marker marker, CancellationToken ct)
    {
        if (marker.ProjectId is { Length: > 0 } pid)
        {
            var project = await store.GetProjectAsync(pid, ct);
            return new ScopeResolution(ScopeSource.BrainFile, pid, project?.Slug, marker.Path);
        }

        if (marker.Slug is { Length: > 0 } slug)
        {
            var project = await store.GetProjectBySlugAsync(slug, ct);
            return project is not null
                ? new ScopeResolution(ScopeSource.BrainFile, project.Id, project.Slug, marker.Path)
                // Marker references a slug that has not been registered yet.
                // We treat that as "user intent is clear, project just missing";
                // caller can offer `brainz init` to register. Fall through to
                // global so queries still work.
                : null;
        }

        // Empty .brain file — ignore and fall through.
        return null;
    }
}
