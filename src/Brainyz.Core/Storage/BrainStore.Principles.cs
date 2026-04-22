// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddPrincipleAsync(Principle p, CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO principles
                (id, project_id, title, statement, rationale, active, created_at, updated_at)
            VALUES (@id, @pid, @title, @st, @rat, @active, @created, @updated)
            """;
        cmd.Bind("@id", p.Id);
        cmd.Bind("@pid", p.ProjectId);
        cmd.Bind("@title", p.Title);
        cmd.Bind("@st", p.Statement);
        cmd.Bind("@rat", p.Rationale);
        cmd.Bind("@active", p.Active ? 1 : 0);
        cmd.Bind("@created", p.CreatedAtMs == 0 ? now : p.CreatedAtMs);
        cmd.Bind("@updated", p.UpdatedAtMs == 0 ? now : p.UpdatedAtMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> UpdatePrincipleAsync(Principle p, CancellationToken ct = default)
    {
        if (!await ExistsAsync("principles", p.Id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE principles SET
                project_id = @pid,
                title      = @title,
                statement  = @st,
                rationale  = @rat,
                active     = @active,
                updated_at = @updated
            WHERE id = @id
            """;
        // Bind order must match SQL order — Nelknet 0.2.5 binds positionally
        // and silently mismatches names. Keep this ordering stable.
        cmd.Bind("@pid", p.ProjectId);
        cmd.Bind("@title", p.Title);
        cmd.Bind("@st", p.Statement);
        cmd.Bind("@rat", p.Rationale);
        cmd.Bind("@active", p.Active ? 1 : 0);
        cmd.Bind("@updated", _clock.NowMs());
        cmd.Bind("@id", p.Id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<bool> DeletePrincipleAsync(string id, CancellationToken ct = default)
    {
        if (!await ExistsAsync("principles", id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM principles WHERE id = @id";
        cmd.Bind("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<Principle?> GetPrincipleAsync(string id, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, statement, rationale, active, created_at, updated_at
            FROM principles WHERE id = @id
            """;
        cmd.Bind("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? ReadPrinciple(rdr) : null;
    }

    /// <summary>
    /// Lists principles. When <paramref name="projectId"/> is null the query
    /// returns global principles only (rows where <c>project_id IS NULL</c>).
    /// Use <see cref="ListAllPrinciplesAsync"/> to list across all scopes.
    /// </summary>
    public async Task<IReadOnlyList<Principle>> ListPrinciplesAsync(
        string? projectId,
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = projectId is null
            ? """
              SELECT id, project_id, title, statement, rationale, active, created_at, updated_at
              FROM principles
              WHERE project_id IS NULL AND (@active = 0 OR active = 1)
              ORDER BY updated_at DESC
              """
            : """
              SELECT id, project_id, title, statement, rationale, active, created_at, updated_at
              FROM principles
              WHERE project_id = @pid AND (@active = 0 OR active = 1)
              ORDER BY updated_at DESC
              """;
        if (projectId is not null) cmd.Bind("@pid", projectId);
        cmd.Bind("@active", activeOnly ? 1 : 0);
        return await ReadPrinciplesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Principle>> ListAllPrinciplesAsync(
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, statement, rationale, active, created_at, updated_at
            FROM principles
            WHERE (@active = 0 OR active = 1)
            ORDER BY updated_at DESC
            """;
        cmd.Bind("@active", activeOnly ? 1 : 0);
        return await ReadPrinciplesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Principle>> SearchPrinciplesAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.project_id, p.title, p.statement, p.rationale, p.active,
                   p.created_at, p.updated_at
            FROM principles_fts f
            JOIN principles p ON p.id = f.principle_id
            WHERE principles_fts MATCH @q
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Bind("@q", query);
        cmd.Bind("@limit", limit);
        return await ReadPrinciplesAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<Principle>> ReadPrinciplesAsync(
        System.Data.Common.DbCommand cmd, CancellationToken ct)
    {
        var results = new List<Principle>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadPrinciple(rdr));
        return results;
    }

    private static Principle ReadPrinciple(System.Data.Common.DbDataReader rdr) => new(
        Id: rdr.GetStringSafe(0),
        ProjectId: rdr.GetStringOrNull(1),
        Title: rdr.GetStringSafe(2),
        Statement: rdr.GetStringSafe(3),
        Rationale: rdr.GetStringOrNull(4),
        Active: rdr.GetInt64Safe(5) != 0,
        CreatedAtMs: rdr.GetInt64Safe(6),
        UpdatedAtMs: rdr.GetInt64Safe(7));
}
