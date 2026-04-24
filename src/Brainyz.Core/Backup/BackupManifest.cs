// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Backup;

/// <summary>
/// Metadata written as <c>manifest.json</c> inside every backup ZIP.
/// Matches §5.2 of the v0.3 spec.
/// </summary>
public sealed record BackupManifest(
    int FormatVersion,                          // 1 in v0.3
    string BrainyzVersion,                      // e.g. "0.3.0"
    int SchemaVersion,
    long CreatedAtMs,
    string SourceDbPath,
    string SourceHostname,
    long DbSizeBytes,
    string? EmbeddingModel,
    Dictionary<string, int> Counts
);
