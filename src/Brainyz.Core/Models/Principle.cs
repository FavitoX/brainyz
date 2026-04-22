// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

public sealed record Principle(
    string Id,
    string? ProjectId,
    string Title,
    string Statement,
    string? Rationale = null,
    bool Active = true,
    long CreatedAtMs = 0,
    long UpdatedAtMs = 0);
