// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Models;

public enum DecisionStatus { Proposed, Accepted, Deprecated, Superseded }

public enum Confidence { Low, Medium, High }

public sealed record Decision(
    string Id,
    string? ProjectId,
    string Title,
    string Text,
    string? Context = null,
    string? Rationale = null,
    string? Consequences = null,
    DecisionStatus Status = DecisionStatus.Accepted,
    Confidence? Confidence = null,
    long? RevisitAtMs = null,
    long CreatedAtMs = 0,
    long UpdatedAtMs = 0);

public sealed record Alternative(
    string Id,
    string DecisionId,
    string Title,
    string WhyRejected,
    string? Description = null,
    int SortOrder = 0,
    long CreatedAtMs = 0);
