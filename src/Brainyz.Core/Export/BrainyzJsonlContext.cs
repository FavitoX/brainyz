// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using Brainyz.Core.Backup;

namespace Brainyz.Core.Export;

/// <summary>
/// Source-generated serialization contract for the JSONL export/import
/// and the backup manifest. Kept separate from
/// <c>Brainyz.Cli.Mcp.BrainyzJsonContext</c> so the JSONL format can use
/// snake_case + explicit nulls without affecting MCP tool serialization
/// (which deliberately uses camelCase + omit-null).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false)]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(JsonlProjectRecord))]
[JsonSerializable(typeof(JsonlProjectRemoteRecord))]
[JsonSerializable(typeof(JsonlDecisionRecord))]
[JsonSerializable(typeof(JsonlAlternativeRecord))]
[JsonSerializable(typeof(JsonlPrincipleRecord))]
[JsonSerializable(typeof(JsonlNoteRecord))]
[JsonSerializable(typeof(JsonlTagRecord))]
[JsonSerializable(typeof(JsonlTagLinkRecord))]
[JsonSerializable(typeof(JsonlLinkRecord))]
[JsonSerializable(typeof(JsonlEmbeddingRecord))]
[JsonSerializable(typeof(JsonlHistoryRecord))]
internal sealed partial class BrainyzJsonlContext : JsonSerializerContext;
