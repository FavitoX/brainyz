// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;
using Brainyz.Core.Scope;

namespace Brainyz.Cli.Mcp;

/// <summary>
/// Translators between Core domain records and MCP wire DTOs. Centralised so
/// the tool class stays readable and the enum-to-string mapping lives in a
/// single place.
/// </summary>
internal static class Mapping
{
    public static ScopeInfo ToDto(this ScopeResolution s) => new(
        Source: SourceLabel(s.Source),
        ProjectId: s.ProjectId,
        ProjectSlug: s.ProjectSlug,
        Hint: s.Hint);

    public static DecisionInfo ToDto(this Decision d) => new(
        Id: d.Id,
        ProjectId: d.ProjectId,
        Title: d.Title,
        Text: d.Text,
        Status: d.Status.ToString().ToLowerInvariant(),
        Confidence: d.Confidence?.ToString().ToLowerInvariant(),
        Context: d.Context,
        Rationale: d.Rationale,
        Consequences: d.Consequences,
        RevisitAtMs: d.RevisitAtMs,
        CreatedAtMs: d.CreatedAtMs,
        UpdatedAtMs: d.UpdatedAtMs);

    public static PrincipleInfo ToDto(this Principle p) => new(
        Id: p.Id,
        ProjectId: p.ProjectId,
        Title: p.Title,
        Statement: p.Statement,
        Rationale: p.Rationale,
        Active: p.Active,
        CreatedAtMs: p.CreatedAtMs,
        UpdatedAtMs: p.UpdatedAtMs);

    public static NoteInfo ToDto(this Note n) => new(
        Id: n.Id,
        ProjectId: n.ProjectId,
        Title: n.Title,
        Content: n.Content,
        Source: n.Source,
        Active: n.Active,
        CreatedAtMs: n.CreatedAtMs,
        UpdatedAtMs: n.UpdatedAtMs);

    public static ProjectInfo ToDto(this Project p) => new(
        Id: p.Id,
        Slug: p.Slug,
        Name: p.Name,
        Description: p.Description,
        CreatedAtMs: p.CreatedAtMs,
        UpdatedAtMs: p.UpdatedAtMs);

    public static LinkInfo ToDto(this Link l) => new(
        Id: l.Id,
        FromType: EntityLabel(l.FromType),
        FromId: l.FromId,
        ToType: EntityLabel(l.ToType),
        ToId: l.ToId,
        Relation: RelationLabel(l.Relation),
        Annotation: l.Annotation,
        CreatedAtMs: l.CreatedAtMs);

    public static string SourceLabel(ScopeSource s) => s switch
    {
        ScopeSource.BrainFile => "brain-file",
        ScopeSource.GitRemote => "git-remote",
        ScopeSource.ConfigPathMap => "config-path-map",
        ScopeSource.Global => "global",
        _ => "unknown"
    };

    public static string EntityLabel(LinkEntity e) => e switch
    {
        LinkEntity.Decision => "decision",
        LinkEntity.Principle => "principle",
        LinkEntity.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    public static string RelationLabel(LinkRelation r) => r switch
    {
        LinkRelation.Supersedes => "supersedes",
        LinkRelation.RelatesTo => "relates_to",
        LinkRelation.DependsOn => "depends_on",
        LinkRelation.ConflictsWith => "conflicts_with",
        LinkRelation.InformedBy => "informed_by",
        LinkRelation.DerivedFrom => "derived_from",
        LinkRelation.SplitFrom => "split_from",
        LinkRelation.Contradicts => "contradicts",
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    public static bool TryParseRelation(string raw, out LinkRelation relation)
    {
        var normalized = raw.Trim().ToLowerInvariant().Replace('-', '_');
        relation = LinkRelation.RelatesTo;
        switch (normalized)
        {
            case "supersedes":      relation = LinkRelation.Supersedes;     return true;
            case "relates_to":      relation = LinkRelation.RelatesTo;      return true;
            case "depends_on":      relation = LinkRelation.DependsOn;      return true;
            case "conflicts_with":  relation = LinkRelation.ConflictsWith;  return true;
            case "informed_by":     relation = LinkRelation.InformedBy;     return true;
            case "derived_from":    relation = LinkRelation.DerivedFrom;    return true;
            case "split_from":      relation = LinkRelation.SplitFrom;      return true;
            case "contradicts":     relation = LinkRelation.Contradicts;    return true;
        }
        return false;
    }

    public static bool TryParseConfidence(string? raw, out Confidence? confidence)
    {
        confidence = null;
        if (string.IsNullOrEmpty(raw)) return true;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "low": confidence = Confidence.Low; return true;
            case "medium": confidence = Confidence.Medium; return true;
            case "high": confidence = Confidence.High; return true;
        }
        return false;
    }

    public static bool TryParseStatus(string? raw, out DecisionStatus status)
    {
        status = DecisionStatus.Accepted;
        if (string.IsNullOrEmpty(raw)) return true;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "proposed":    status = DecisionStatus.Proposed;    return true;
            case "accepted":    status = DecisionStatus.Accepted;    return true;
            case "deprecated":  status = DecisionStatus.Deprecated;  return true;
            case "superseded":  status = DecisionStatus.Superseded;  return true;
        }
        return false;
    }
}
