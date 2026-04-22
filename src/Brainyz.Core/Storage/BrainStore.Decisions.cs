// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddDecisionAsync(
        Decision d,
        IReadOnlyList<Alternative>? alternatives = null,
        CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        using var tx = _conn.BeginTransaction();

        await using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO decisions
                    (id, project_id, title, status, confidence,
                     context, decision, rationale, consequences,
                     revisit_at, created_at, updated_at)
                VALUES (@id, @pid, @title, @status, @conf,
                        @ctx, @dec, @rat, @cons,
                        @revisit, @created, @updated)
                """;
            cmd.Bind("@id", d.Id);
            cmd.Bind("@pid", d.ProjectId);
            cmd.Bind("@title", d.Title);
            cmd.Bind("@status", EncodeStatus(d.Status));
            cmd.Bind("@conf", EncodeConfidence(d.Confidence));
            cmd.Bind("@ctx", d.Context);
            cmd.Bind("@dec", d.Text);
            cmd.Bind("@rat", d.Rationale);
            cmd.Bind("@cons", d.Consequences);
            cmd.Bind("@revisit", (object?)d.RevisitAtMs);
            cmd.Bind("@created", d.CreatedAtMs == 0 ? now : d.CreatedAtMs);
            cmd.Bind("@updated", d.UpdatedAtMs == 0 ? now : d.UpdatedAtMs);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (alternatives is { Count: > 0 })
        {
            foreach (var alt in alternatives)
            {
                await using var altCmd = _conn.CreateCommand();
                altCmd.Transaction = tx;
                altCmd.CommandText = """
                    INSERT INTO alternatives
                        (id, decision_id, title, description, why_rejected, sort_order, created_at)
                    VALUES (@id, @did, @title, @desc, @why, @sort, @created)
                    """;
                altCmd.Bind("@id", alt.Id);
                altCmd.Bind("@did", d.Id);
                altCmd.Bind("@title", alt.Title);
                altCmd.Bind("@desc", alt.Description);
                altCmd.Bind("@why", alt.WhyRejected);
                altCmd.Bind("@sort", alt.SortOrder);
                altCmd.Bind("@created", alt.CreatedAtMs == 0 ? now : alt.CreatedAtMs);
                await altCmd.ExecuteNonQueryAsync(ct);
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// Replaces every mutable column of a decision by id. The update trigger
    /// copies the previous row into <c>decisions_history</c> automatically.
    /// Returns true when a row matched; false means the id did not exist.
    /// </summary>
    public async Task<bool> UpdateDecisionAsync(Decision d, CancellationToken ct = default)
    {
        // Nelknet 0.2.5 does not expose a reliable affected-rows count
        // (ExecuteNonQueryAsync returns -1, ExecuteReaderAsync over
        // UPDATE … RETURNING delivers zero rows). We pre-check existence
        // instead and skip the UPDATE when the id is unknown.
        if (!await ExistsAsync("decisions", d.Id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE decisions SET
                project_id   = @pid,
                title        = @title,
                status       = @status,
                confidence   = @conf,
                context      = @ctx,
                decision     = @dec,
                rationale    = @rat,
                consequences = @cons,
                revisit_at   = @revisit,
                updated_at   = @updated
            WHERE id = @id
            """;
        // Bind order must match SQL order — Nelknet 0.2.5 binds positionally
        // and silently mismatches by name.
        cmd.Bind("@pid", d.ProjectId);
        cmd.Bind("@title", d.Title);
        cmd.Bind("@status", EncodeStatus(d.Status));
        cmd.Bind("@conf", EncodeConfidence(d.Confidence));
        cmd.Bind("@ctx", d.Context);
        cmd.Bind("@dec", d.Text);
        cmd.Bind("@rat", d.Rationale);
        cmd.Bind("@cons", d.Consequences);
        cmd.Bind("@revisit", (object?)d.RevisitAtMs);
        cmd.Bind("@updated", _clock.NowMs());
        cmd.Bind("@id", d.Id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>
    /// Deletes a decision and — via schema triggers — its alternatives,
    /// embeddings, tag bindings, and incoming/outgoing links. The deletion
    /// is snapshotted into <c>decisions_history</c> first.
    /// </summary>
    public async Task<bool> DeleteDecisionAsync(string id, CancellationToken ct = default)
    {
        if (!await ExistsAsync("decisions", id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM decisions WHERE id = @id";
        cmd.Bind("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<Decision?> GetDecisionAsync(string id, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, status, confidence,
                   context, decision, rationale, consequences,
                   revisit_at, created_at, updated_at
            FROM decisions WHERE id = @id
            """;
        cmd.Bind("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? ReadDecision(rdr) : null;
    }

    public async Task<IReadOnlyList<Alternative>> GetAlternativesAsync(
        string decisionId, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, decision_id, title, description, why_rejected, sort_order, created_at
            FROM alternatives WHERE decision_id = @did
            ORDER BY sort_order, created_at
            """;
        cmd.Bind("@did", decisionId);
        var results = new List<Alternative>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            results.Add(new Alternative(
                Id: rdr.GetStringSafe(0),
                DecisionId: rdr.GetStringSafe(1),
                Title: rdr.GetStringSafe(2),
                Description: rdr.GetStringOrNull(3),
                WhyRejected: rdr.GetStringSafe(4),
                SortOrder: (int)rdr.GetInt64Safe(5),
                CreatedAtMs: rdr.GetInt64Safe(6)));
        }
        return results;
    }

    public async Task<IReadOnlyList<Decision>> ListDecisionsAsync(
        string? projectId = null,
        bool includeGlobal = true,
        int limit = 50,
        CancellationToken ct = default)
    {
        // projectId null + includeGlobal true  → all global decisions
        // projectId set  + includeGlobal true  → decisions in that project + globals
        // projectId set  + includeGlobal false → decisions in that project only
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = (projectId, includeGlobal) switch
        {
            (null, _) => """
                SELECT id, project_id, title, status, confidence,
                       context, decision, rationale, consequences,
                       revisit_at, created_at, updated_at
                FROM decisions
                WHERE project_id IS NULL
                ORDER BY updated_at DESC
                LIMIT @limit
                """,
            (_, true) => """
                SELECT id, project_id, title, status, confidence,
                       context, decision, rationale, consequences,
                       revisit_at, created_at, updated_at
                FROM decisions
                WHERE project_id = @pid OR project_id IS NULL
                ORDER BY updated_at DESC
                LIMIT @limit
                """,
            (_, false) => """
                SELECT id, project_id, title, status, confidence,
                       context, decision, rationale, consequences,
                       revisit_at, created_at, updated_at
                FROM decisions
                WHERE project_id = @pid
                ORDER BY updated_at DESC
                LIMIT @limit
                """
        };
        if (projectId is not null) cmd.Bind("@pid", projectId);
        cmd.Bind("@limit", limit);

        var results = new List<Decision>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadDecision(rdr));
        return results;
    }

    public async Task<IReadOnlyList<Decision>> ListAllDecisionsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, status, confidence,
                   context, decision, rationale, consequences,
                   revisit_at, created_at, updated_at
            FROM decisions
            ORDER BY updated_at DESC
            LIMIT @limit
            """;
        cmd.Bind("@limit", limit);

        var results = new List<Decision>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadDecision(rdr));
        return results;
    }

    public async Task<IReadOnlyList<Decision>> SearchDecisionsAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default)
    {
        // FTS5 MATCH. The caller is expected to pass a sanitized query —
        // escaping of FTS5 operators belongs in a dedicated SearchService.
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.id, d.project_id, d.title, d.status, d.confidence,
                   d.context, d.decision, d.rationale, d.consequences,
                   d.revisit_at, d.created_at, d.updated_at
            FROM decisions_fts f
            JOIN decisions d ON d.id = f.decision_id
            WHERE decisions_fts MATCH @q
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Bind("@q", query);
        cmd.Bind("@limit", limit);
        var results = new List<Decision>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadDecision(rdr));
        return results;
    }

    private static Decision ReadDecision(System.Data.Common.DbDataReader rdr) => new(
        Id: rdr.GetStringSafe(0),
        ProjectId: rdr.GetStringOrNull(1),
        Title: rdr.GetStringSafe(2),
        Status: DecodeStatus(rdr.GetStringSafe(3)),
        Confidence: rdr.GetStringOrNull(4) is { } c ? DecodeConfidence(c) : null,
        Context: rdr.GetStringOrNull(5),
        Text: rdr.GetStringSafe(6),
        Rationale: rdr.GetStringOrNull(7),
        Consequences: rdr.GetStringOrNull(8),
        RevisitAtMs: rdr.GetInt64OrNull(9),
        CreatedAtMs: rdr.GetInt64Safe(10),
        UpdatedAtMs: rdr.GetInt64Safe(11));

    private static string EncodeStatus(DecisionStatus s) => s switch
    {
        DecisionStatus.Proposed => "proposed",
        DecisionStatus.Accepted => "accepted",
        DecisionStatus.Deprecated => "deprecated",
        DecisionStatus.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(nameof(s))
    };

    private static DecisionStatus DecodeStatus(string s) => s switch
    {
        "proposed" => DecisionStatus.Proposed,
        "accepted" => DecisionStatus.Accepted,
        "deprecated" => DecisionStatus.Deprecated,
        "superseded" => DecisionStatus.Superseded,
        _ => throw new InvalidDataException($"Unknown status: '{s}'")
    };

    private static string? EncodeConfidence(Confidence? c) => c switch
    {
        null => null,
        Confidence.Low => "low",
        Confidence.Medium => "medium",
        Confidence.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(c))
    };

    private static Confidence DecodeConfidence(string s) => s switch
    {
        "low" => Confidence.Low,
        "medium" => Confidence.Medium,
        "high" => Confidence.High,
        _ => throw new InvalidDataException($"Unknown confidence: '{s}'")
    };
}
