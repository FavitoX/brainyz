// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Export;

/// <summary>
/// First record of every JSONL export (<c>type: "manifest"</c>).
/// Matches §3.3 of the v0.3 spec. Serialized with snake_case so property
/// names here map to <c>exported_at_ms</c>, <c>format_version</c>, etc.
/// </summary>
public sealed record ExportManifest(
    string Type,                                // always "manifest"
    int FormatVersion,                          // 1 in v0.3
    string BrainyzVersion,                      // e.g. "0.3.0"
    int SchemaVersion,
    long ExportedAtMs,
    string? ProjectFilter,                      // null = whole brain
    bool IncludeHistory,
    Dictionary<string, int> Counts
);
