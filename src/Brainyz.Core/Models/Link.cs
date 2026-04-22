// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

public enum LinkEntity { Decision, Principle, Note }

public enum LinkRelation
{
    Supersedes,
    RelatesTo,
    DependsOn,
    ConflictsWith,
    InformedBy,
    DerivedFrom,
    SplitFrom,
    Contradicts
}

public sealed record Link(
    string Id,
    LinkEntity FromType,
    string FromId,
    LinkEntity ToType,
    string ToId,
    LinkRelation Relation,
    string? Annotation = null,
    long CreatedAtMs = 0);
