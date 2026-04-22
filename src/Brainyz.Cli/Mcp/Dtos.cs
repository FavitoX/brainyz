// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Cli.Mcp;

// MCP responses — kept as simple records with string-typed enums so agents
// see readable values instead of integer codes. Core models stay pure; these
// DTOs are the wire contract the MCP server exposes.

public sealed record ScopeInfo(
    string Source,
    string? ProjectId,
    string? ProjectSlug,
    string? Hint);

public sealed record DecisionInfo(
    string Id,
    string? ProjectId,
    string Title,
    string Text,
    string Status,
    string? Confidence,
    string? Context,
    string? Rationale,
    string? Consequences,
    long? RevisitAtMs,
    long CreatedAtMs,
    long UpdatedAtMs);

public sealed record PrincipleInfo(
    string Id,
    string? ProjectId,
    string Title,
    string Statement,
    string? Rationale,
    bool Active,
    long CreatedAtMs,
    long UpdatedAtMs);

public sealed record NoteInfo(
    string Id,
    string? ProjectId,
    string Title,
    string Content,
    string? Source,
    bool Active,
    long CreatedAtMs,
    long UpdatedAtMs);

public sealed record ProjectInfo(
    string Id,
    string Slug,
    string Name,
    string? Description,
    long CreatedAtMs,
    long UpdatedAtMs);

public sealed record SearchHit(
    string Type,
    string Id,
    string? ProjectId,
    string Title);

public sealed record LinkInfo(
    string Id,
    string FromType,
    string FromId,
    string ToType,
    string ToId,
    string Relation,
    string? Annotation,
    long CreatedAtMs);

/// <summary>
/// Generic result wrapper used when a tool needs to return either a typed
/// entity or a null marker (e.g. <c>Get(id)</c> when nothing matches).
/// </summary>
public sealed record EntityResult(
    string? Type,          // "decision" | "principle" | "note" | null when not found
    DecisionInfo? Decision,
    PrincipleInfo? Principle,
    NoteInfo? Note);
