// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Import;

public enum ImportMode { Replace, Additive }

/// <summary>
/// Result of a JSONL import. <paramref name="InsertedCounts"/> holds
/// rows newly INSERTed keyed by record <c>type</c>.
/// <paramref name="SkippedCounts"/> is populated only in additive mode
/// (rows whose ULID already existed). <paramref name="Warnings"/> holds
/// non-fatal issues surfaced during the integrity check and the FTS
/// rebuild. <paramref name="RowCountMismatch"/> is set only when replace
/// mode finds a divergence between manifest counts and actual post-commit
/// counts — the CLI bumps the exit code to 1 in that case per §4.4 of
/// the v0.3 spec.
/// </summary>
public sealed record ImportResult(
    ImportMode Mode,
    Dictionary<string, int> InsertedCounts,
    Dictionary<string, int> SkippedCounts,
    List<string> Warnings,
    bool RowCountMismatch,
    string? SafetyBackupPath);
