// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

public sealed record Note(
    string Id,
    string? ProjectId,
    string Title,
    string Content,
    string? Source = null,
    bool Active = true,
    long CreatedAtMs = 0,
    long UpdatedAtMs = 0);
