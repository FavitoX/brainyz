// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;
using Brainyz.Core.Scope;

namespace Brainyz.Cli;

/// <summary>
/// Helpers that translate a CLI <c>--project</c> flag into a concrete project
/// id. Commands either honour the flag (explicit slug overrides detection),
/// fall back to <see cref="ScopeResolver"/> when omitted, or stay global when
/// resolution yields nothing.
/// </summary>
internal static class Scoping
{
    /// <summary>
    /// Resolves the effective project id for a mutating command. When
    /// <paramref name="explicitSlug"/> is set, the project MUST exist — an
    /// empty slug registered later would silently drop the write on the floor.
    /// </summary>
    public static async Task<(string? projectId, string? error)> ResolveForWriteAsync(
        BrainContext ctx,
        string? explicitSlug,
        string cwd,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(explicitSlug))
        {
            var project = await ctx.Store.GetProjectBySlugAsync(explicitSlug, ct);
            if (project is null)
                return (null, $"project '{explicitSlug}' not found. Register it with: brainz init --project {explicitSlug}");
            return (project.Id, null);
        }

        var scope = await ctx.Resolver.ResolveAsync(cwd, ct);
        return (scope.ProjectId, null);
    }
}
