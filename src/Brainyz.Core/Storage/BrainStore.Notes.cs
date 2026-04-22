// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddNoteAsync(Note n, CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes
                (id, project_id, title, content, source, active, created_at, updated_at)
            VALUES (@id, @pid, @title, @content, @source, @active, @created, @updated)
            """;
        cmd.Bind("@id", n.Id);
        cmd.Bind("@pid", n.ProjectId);
        cmd.Bind("@title", n.Title);
        cmd.Bind("@content", n.Content);
        cmd.Bind("@source", n.Source);
        cmd.Bind("@active", n.Active ? 1 : 0);
        cmd.Bind("@created", n.CreatedAtMs == 0 ? now : n.CreatedAtMs);
        cmd.Bind("@updated", n.UpdatedAtMs == 0 ? now : n.UpdatedAtMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> UpdateNoteAsync(Note n, CancellationToken ct = default)
    {
        if (!await ExistsAsync("notes", n.Id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE notes SET
                project_id = @pid,
                title      = @title,
                content    = @content,
                source     = @source,
                active     = @active,
                updated_at = @updated
            WHERE id = @id
            """;
        // Bind order must match SQL order — Nelknet 0.2.5 binds positionally.
        cmd.Bind("@pid", n.ProjectId);
        cmd.Bind("@title", n.Title);
        cmd.Bind("@content", n.Content);
        cmd.Bind("@source", n.Source);
        cmd.Bind("@active", n.Active ? 1 : 0);
        cmd.Bind("@updated", _clock.NowMs());
        cmd.Bind("@id", n.Id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<bool> DeleteNoteAsync(string id, CancellationToken ct = default)
    {
        if (!await ExistsAsync("notes", id, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE id = @id";
        cmd.Bind("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<Note?> GetNoteAsync(string id, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, content, source, active, created_at, updated_at
            FROM notes WHERE id = @id
            """;
        cmd.Bind("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? ReadNote(rdr) : null;
    }

    public async Task<IReadOnlyList<Note>> ListNotesAsync(
        string? projectId = null,
        bool includeGlobal = true,
        bool activeOnly = true,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        var scopeClause = (projectId, includeGlobal) switch
        {
            (null, _)   => "project_id IS NULL",
            (_, true)   => "(project_id = @pid OR project_id IS NULL)",
            (_, false)  => "project_id = @pid"
        };
        cmd.CommandText = $"""
            SELECT id, project_id, title, content, source, active, created_at, updated_at
            FROM notes
            WHERE {scopeClause} AND (@active = 0 OR active = 1)
            ORDER BY updated_at DESC
            LIMIT @limit
            """;
        if (projectId is not null) cmd.Bind("@pid", projectId);
        cmd.Bind("@active", activeOnly ? 1 : 0);
        cmd.Bind("@limit", limit);

        var results = new List<Note>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadNote(rdr));
        return results;
    }

    public async Task<IReadOnlyList<Note>> ListAllNotesAsync(
        bool activeOnly = true,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, title, content, source, active, created_at, updated_at
            FROM notes
            WHERE (@active = 0 OR active = 1)
            ORDER BY updated_at DESC
            LIMIT @limit
            """;
        cmd.Bind("@active", activeOnly ? 1 : 0);
        cmd.Bind("@limit", limit);

        var results = new List<Note>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadNote(rdr));
        return results;
    }

    public async Task<IReadOnlyList<Note>> SearchNotesAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.id, n.project_id, n.title, n.content, n.source, n.active,
                   n.created_at, n.updated_at
            FROM notes_fts f
            JOIN notes n ON n.id = f.note_id
            WHERE notes_fts MATCH @q
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Bind("@q", query);
        cmd.Bind("@limit", limit);
        var results = new List<Note>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadNote(rdr));
        return results;
    }

    private static Note ReadNote(System.Data.Common.DbDataReader rdr) => new(
        Id: rdr.GetStringSafe(0),
        ProjectId: rdr.GetStringOrNull(1),
        Title: rdr.GetStringSafe(2),
        Content: rdr.GetStringSafe(3),
        Source: rdr.GetStringOrNull(4),
        Active: rdr.GetInt64Safe(5) != 0,
        CreatedAtMs: rdr.GetInt64Safe(6),
        UpdatedAtMs: rdr.GetInt64Safe(7));
}
