// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Scope;

/// <summary>
/// Where the scope resolution came from. Listed in resolution-order priority
/// (highest first); <see cref="Global"/> is the fallback when no rule matched.
/// </summary>
public enum ScopeSource
{
    BrainFile,
    GitRemote,
    ConfigPathMap,
    Global
}

/// <summary>
/// Outcome of <see cref="ScopeResolver"/>. When <see cref="ProjectId"/> is null
/// the caller should treat the query as global (no <c>project_id</c> filter).
/// </summary>
/// <param name="Source">Which rule produced the result.</param>
/// <param name="ProjectId">Resolved project id; null when <see cref="Source"/> is <see cref="ScopeSource.Global"/>.</param>
/// <param name="ProjectSlug">Human-readable slug of the project, when known.</param>
/// <param name="Hint">Path or URL that triggered the match — useful for <c>resolve_scope</c> debugging.</param>
public sealed record ScopeResolution(
    ScopeSource Source,
    string? ProjectId,
    string? ProjectSlug = null,
    string? Hint = null);
