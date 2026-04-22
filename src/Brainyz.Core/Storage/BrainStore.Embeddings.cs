// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    /// <summary>
    /// Inserts or replaces an embedding for a decision. Idempotent on
    /// <c>(decision_id, model)</c>; used to regenerate vectors when the
    /// underlying text or the model changes.
    /// </summary>
    public Task UpsertDecisionEmbeddingAsync(
        string decisionId,
        string model,
        ReadOnlyMemory<float> vector,
        string contentHash,
        CancellationToken ct = default) =>
        UpsertEmbeddingAsync("decision_embeddings", "decision_id", decisionId, model, vector, contentHash, ct);

    public Task UpsertPrincipleEmbeddingAsync(
        string principleId, string model, ReadOnlyMemory<float> vector, string contentHash,
        CancellationToken ct = default) =>
        UpsertEmbeddingAsync("principle_embeddings", "principle_id", principleId, model, vector, contentHash, ct);

    public Task UpsertNoteEmbeddingAsync(
        string noteId, string model, ReadOnlyMemory<float> vector, string contentHash,
        CancellationToken ct = default) =>
        UpsertEmbeddingAsync("note_embeddings", "note_id", noteId, model, vector, contentHash, ct);

    public Task<string?> GetDecisionContentHashAsync(string id, string model, CancellationToken ct = default) =>
        GetContentHashAsync("decision_embeddings", "decision_id", id, model, ct);

    public Task<string?> GetPrincipleContentHashAsync(string id, string model, CancellationToken ct = default) =>
        GetContentHashAsync("principle_embeddings", "principle_id", id, model, ct);

    public Task<string?> GetNoteContentHashAsync(string id, string model, CancellationToken ct = default) =>
        GetContentHashAsync("note_embeddings", "note_id", id, model, ct);

    public Task<IReadOnlyList<(string EntityId, double Distance)>> SearchDecisionsByVectorAsync(
        ReadOnlyMemory<float> vector, string model, int limit = 20, CancellationToken ct = default) =>
        SearchByVectorAsync("decision_embeddings", "decision_id", vector, model, limit, ct);

    public Task<IReadOnlyList<(string EntityId, double Distance)>> SearchPrinciplesByVectorAsync(
        ReadOnlyMemory<float> vector, string model, int limit = 20, CancellationToken ct = default) =>
        SearchByVectorAsync("principle_embeddings", "principle_id", vector, model, limit, ct);

    public Task<IReadOnlyList<(string EntityId, double Distance)>> SearchNotesByVectorAsync(
        ReadOnlyMemory<float> vector, string model, int limit = 20, CancellationToken ct = default) =>
        SearchByVectorAsync("note_embeddings", "note_id", vector, model, limit, ct);

    // ───────── private SQL helpers ─────────

    // Table/column names come from a fixed allow-list; string interpolation
    // is safe here and lets us keep a single implementation for three tables.
    private async Task UpsertEmbeddingAsync(
        string table,
        string idColumn,
        string entityId,
        string model,
        ReadOnlyMemory<float> vector,
        string contentHash,
        CancellationToken ct)
    {
        var literal = VectorLiteral(vector.Span);

        using var tx = _conn.BeginTransaction();

        // Nelknet doesn't implement INSERT OR REPLACE nicely with vector32(),
        // so we delete-then-insert inside a transaction.
        await using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {table} WHERE {idColumn} = @id AND model = @model";
            del.Bind("@id", entityId);
            del.Bind("@model", model);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using (var ins = _conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = $"""
                INSERT INTO {table} ({idColumn}, model, dim, vector, content_hash, created_at)
                VALUES (@id, @model, @dim, vector32(@vec), @hash, @created)
                """;
            ins.Bind("@id", entityId);
            ins.Bind("@model", model);
            ins.Bind("@dim", vector.Length);
            ins.Bind("@vec", literal);
            ins.Bind("@hash", contentHash);
            ins.Bind("@created", _clock.NowMs());
            await ins.ExecuteNonQueryAsync(ct);
        }

        tx.Commit();
    }

    private async Task<string?> GetContentHashAsync(
        string table, string idColumn, string entityId, string model, CancellationToken ct)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT content_hash FROM {table} WHERE {idColumn} = @id AND model = @model";
        cmd.Bind("@id", entityId);
        cmd.Bind("@model", model);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is string s ? s : null;
    }

    private async Task<IReadOnlyList<(string, double)>> SearchByVectorAsync(
        string table, string idColumn,
        ReadOnlyMemory<float> vector, string model, int limit,
        CancellationToken ct)
    {
        var literal = VectorLiteral(vector.Span);

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {idColumn}, vector_distance_cos(vector, vector32(@q)) AS dist
            FROM {table}
            WHERE model = @model
            ORDER BY dist ASC
            LIMIT @limit
            """;
        cmd.Bind("@q", literal);
        cmd.Bind("@model", model);
        cmd.Bind("@limit", limit);

        var results = new List<(string, double)>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var id = rdr.GetStringSafe(0);
            var dist = Convert.ToDouble(rdr.GetValue(1), CultureInfo.InvariantCulture);
            results.Add((id, dist));
        }
        return results;
    }

    private static string VectorLiteral(ReadOnlySpan<float> vector)
    {
        var sb = new StringBuilder(vector.Length * 8);
        sb.Append('[');
        for (int i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
