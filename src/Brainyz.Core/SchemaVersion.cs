// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core;

/// <summary>
/// Schema version the current brainyz build expects. Written into export
/// and backup manifests; validated on import and restore. When a schema
/// migration system ships (post-v0.3), this constant is superseded by a
/// read of the <c>_meta</c> table.
/// </summary>
public static class SchemaVersion
{
    public const int Current = 1;
}
