// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Cli;

/// <summary>
/// Single source of truth for the CLI build version written into the
/// JSONL export manifest and the backup <c>manifest.json</c>. Bump in
/// lockstep with the <c>CHANGELOG.md</c> entry when cutting a release.
/// Kept as a const (not a runtime assembly-metadata read) to stay
/// trim/AOT-friendly.
/// </summary>
internal static class BrainyzCliVersion
{
    public const string Current = "0.3.0";
}
