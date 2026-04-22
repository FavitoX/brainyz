// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

public sealed record Project(
    string Id,
    string Slug,
    string Name,
    string? Description = null,
    long CreatedAtMs = 0,
    long UpdatedAtMs = 0);

public enum RemoteRole { Origin, Upstream, Fork, Mirror, Other }

public sealed record ProjectRemote(
    string Id,
    string ProjectId,
    string RemoteUrl,
    RemoteRole Role = RemoteRole.Origin,
    long CreatedAtMs = 0);
