// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

/// <summary>
/// Hierarchical tag identified by a slash-delimited path (e.g. <c>ailang/auth</c>,
/// <c>payments/idempotency</c>). Unique across the store.
/// </summary>
public sealed record Tag(
    string Id,
    string Path,
    string? Description = null,
    long CreatedAtMs = 0);
