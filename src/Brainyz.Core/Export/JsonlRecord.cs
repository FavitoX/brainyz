// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Export;

// Every record carries a discriminator "type" as its first property.
// Shapes match §3.3 of the v0.3 spec. Timestamps use the `*Ms` C# suffix
// → `*_ms` JSON key after SnakeCaseLower policy.

public sealed record JsonlProjectRecord(
    string Type, string Id, string Slug, string Name, string? Description,
    long CreatedAtMs, long UpdatedAtMs);

public sealed record JsonlProjectRemoteRecord(
    string Type, string Id, string ProjectId, string RemoteUrl, string Role,
    long CreatedAtMs);

public sealed record JsonlDecisionRecord(
    string Type, string Id, string? ProjectId, string Title, string Status,
    string? Confidence, string? Context, string Decision, string? Rationale,
    string? Consequences, long? RevisitAtMs, long CreatedAtMs, long UpdatedAtMs);

public sealed record JsonlAlternativeRecord(
    string Type, string Id, string DecisionId, string Title, string? Description,
    string WhyRejected, int SortOrder, long CreatedAtMs);

public sealed record JsonlPrincipleRecord(
    string Type, string Id, string? ProjectId, string Title, string Statement,
    string? Rationale, int Active, long CreatedAtMs, long UpdatedAtMs);

public sealed record JsonlNoteRecord(
    string Type, string Id, string? ProjectId, string Title, string Content,
    string? Source, int Active, long CreatedAtMs, long UpdatedAtMs);

// schema.sql: `tags` columns are (id, path, description, created_at).
// There is NO `name` or `normalized_name` — do not invent them.
public sealed record JsonlTagRecord(
    string Type, string Id, string Path, string? Description,
    long CreatedAtMs);

// Unifies decision_tags, principle_tags, note_tags.
// entity_type ∈ { "decision", "principle", "note" }
public sealed record JsonlTagLinkRecord(
    string Type, string EntityType, string EntityId, string TagId);

// schema.sql: `links` has a `note` column — include it.
public sealed record JsonlLinkRecord(
    string Type, string Id, string FromType, string FromId,
    string ToType, string ToId, string RelationType, string? Note,
    long CreatedAtMs);

// Unifies decision_embeddings, principle_embeddings, note_embeddings.
public sealed record JsonlEmbeddingRecord(
    string Type, string EntityType, string EntityId, string Model, int Dim,
    string ContentHash, string VectorBase64, long CreatedAtMs);

public sealed record JsonlHistoryRecord(
    string Type, string EntityType, string EntityId, string ChangeType,
    long ChangedAtMs,
    Dictionary<string, object?> Snapshot);
