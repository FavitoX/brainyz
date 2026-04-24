// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;
using Brainyz.Core.Storage;
using Nelknet.LibSQL.Data;

// DbExtensions (Bind) lives in Brainyz.Core.Storage; import it for the
// extension methods. The class itself is internal and reachable via
// InternalsVisibleTo in Brainyz.Core.csproj.

namespace Brainyz.Tests.Export;

/// <summary>
/// Seeds a <see cref="BrainStore"/> with a small but complete brain so
/// export round-trip tests can assert every JSONL record type has at
/// least one row. Two projects are inserted so <c>--project</c> filter
/// tests can see both scopes.
/// </summary>
public static class ExportSeed
{
    public sealed record SeededIds(
        string ProjectId, string OtherProjectId,
        string DecisionId, string OtherDecisionId,
        string PrincipleId, string NoteId,
        string TagId, string LinkId);

    public static async Task<SeededIds> PopulateAsync(BrainStore store, CancellationToken ct = default)
    {
        var project = new Project(
            Id: Ids.NewUlid(),
            Slug: "brainyz",
            Name: "brainyz",
            Description: "Test project for exporter round-trip",
            CreatedAtMs: 1_700_000_000_000,
            UpdatedAtMs: 1_700_000_000_000);
        await store.AddProjectAsync(project, ct);

        var otherProject = new Project(
            Id: Ids.NewUlid(),
            Slug: "other",
            Name: "other",
            Description: "Second project — used to exercise the --project filter",
            CreatedAtMs: 1_700_000_000_100,
            UpdatedAtMs: 1_700_000_000_100);
        await store.AddProjectAsync(otherProject, ct);

        // Project remote on the first project
        await store.AddProjectRemoteAsync(new ProjectRemote(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            RemoteUrl: "git@github.com:FavitoX/brainyz.git",
            Role: RemoteRole.Origin,
            CreatedAtMs: 1_700_000_000_200), ct);

        // Decision in project 1, with alternative
        var decision = new Decision(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            Title: "LibSQL engine",
            Text: "use LibSQL",
            Context: "why SQLite was not enough",
            Rationale: "native vector + sync",
            Consequences: "extra Nelknet gotchas",
            Status: DecisionStatus.Accepted,
            Confidence: Confidence.High,
            CreatedAtMs: 1_700_000_001_000,
            UpdatedAtMs: 1_700_000_001_000);
        var alternatives = new[]
        {
            new Alternative(
                Id: Ids.NewUlid(),
                DecisionId: decision.Id,
                Title: "Pure SQLite",
                WhyRejected: "no native vector support",
                Description: "would require sqlite-vec extension",
                SortOrder: 0,
                CreatedAtMs: 1_700_000_001_500),
        };
        await store.AddDecisionAsync(decision, alternatives, ct);

        // Decision in project 2 — used to verify filter excludes it
        var otherDecision = new Decision(
            Id: Ids.NewUlid(),
            ProjectId: otherProject.Id,
            Title: "Unrelated decision on other project",
            Text: "something else",
            Status: DecisionStatus.Accepted,
            CreatedAtMs: 1_700_000_001_800,
            UpdatedAtMs: 1_700_000_001_800);
        await store.AddDecisionAsync(otherDecision, null, ct);

        // Principle in project 1
        var principle = new Principle(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            Title: "auth mandatory",
            Statement: "every handler authenticates",
            Rationale: "security by default",
            Active: true,
            CreatedAtMs: 1_700_000_002_000,
            UpdatedAtMs: 1_700_000_002_000);
        await store.AddPrincipleAsync(principle, ct);

        // Note in project 1
        var note = new Note(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            Title: "Stripe refund gotcha",
            Content: "v2024-01 changed the behavior of POST /refunds",
            Source: "https://stripe.com/docs/upgrades",
            Active: true,
            CreatedAtMs: 1_700_000_003_000,
            UpdatedAtMs: 1_700_000_003_000);
        await store.AddNoteAsync(note, ct);

        // Tag + link the tag to the decision (via decision_tags)
        var tagId = await store.EnsureTagAsync("resilience", "retries, circuit breakers", ct);
        await store.TagAsync(LinkEntity.Decision, decision.Id, "resilience", ct);

        // Cross-entity link: principle ←derived_from← decision
        var linkId = Ids.NewUlid();
        var link = new Link(
            Id: linkId,
            FromType: LinkEntity.Principle,
            FromId: principle.Id,
            ToType: LinkEntity.Decision,
            ToId: decision.Id,
            Relation: LinkRelation.DerivedFrom,
            Annotation: "principle condensed the decision",
            CreatedAtMs: 1_700_000_005_000);
        await store.AddLinkAsync(link, ct);

        // Embedding on the decision — 768 floats, deterministic
        await InsertDeterministicEmbedding(store.Connection, decision.Id, ct);

        return new SeededIds(
            ProjectId: project.Id,
            OtherProjectId: otherProject.Id,
            DecisionId: decision.Id,
            OtherDecisionId: otherDecision.Id,
            PrincipleId: principle.Id,
            NoteId: note.Id,
            TagId: tagId,
            LinkId: linkId);
    }

    private static async Task InsertDeterministicEmbedding(
        LibSQLConnection conn, string decisionId, CancellationToken ct)
    {
        // Build 768 floats deterministically so round-trip tests can verify
        // the base64 encoding is bit-preserving.
        var floats = new float[768];
        for (int i = 0; i < floats.Length; i++) floats[i] = (float)Math.Sin(i * 0.01);
        var literal = FloatsToLiteral(floats);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO decision_embeddings (decision_id, model, dim, vector, content_hash, created_at)
            VALUES (@id, @model, 768, vector32(@vec), 'sha256:test', @ts)
            """;
        cmd.Bind("@id", decisionId);
        cmd.Bind("@model", "nomic-embed-text:v1.5");
        cmd.Bind("@vec", literal);
        cmd.Bind("@ts", 1_700_000_006_000L);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string FloatsToLiteral(ReadOnlySpan<float> vec)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (int i = 0; i < vec.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vec[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
