// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    /// <summary>
    /// Ensures a tag with the given <paramref name="path"/> exists and returns its id.
    /// If the tag already exists, its description is not modified.
    /// </summary>
    public async Task<string> EnsureTagAsync(
        string path,
        string? description = null,
        CancellationToken ct = default)
    {
        await using var select = _conn.CreateCommand();
        select.CommandText = "SELECT id FROM tags WHERE path = @path";
        select.Bind("@path", path);
        var existing = await select.ExecuteScalarAsync(ct);
        if (existing is string id) return id;

        var newId = Ids.NewUlid();
        await using var insert = _conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO tags (id, path, description, created_at)
            VALUES (@id, @path, @desc, @created)
            """;
        insert.Bind("@id", newId);
        insert.Bind("@path", path);
        insert.Bind("@desc", description);
        insert.Bind("@created", _clock.NowMs());
        await insert.ExecuteNonQueryAsync(ct);
        return newId;
    }

    /// <summary>
    /// Attaches a tag (creating it if needed) to a decision, principle, or note.
    /// Idempotent — tagging the same entity twice with the same tag is a no-op
    /// (relies on the M:N primary key).
    /// </summary>
    public async Task TagAsync(
        LinkEntity entity,
        string entityId,
        string tagPath,
        CancellationToken ct = default)
    {
        var tagId = await EnsureTagAsync(tagPath, ct: ct);
        var (table, idColumn) = TagJoinTable(entity);

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT OR IGNORE INTO {table} ({idColumn}, tag_id)
            VALUES (@eid, @tid)
            """;
        cmd.Bind("@eid", entityId);
        cmd.Bind("@tid", tagId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Removes a tag from an entity. The tag itself stays in the catalog —
    /// use <see cref="DeleteTagAsync"/> to remove the tag entirely.
    /// </summary>
    public async Task<bool> UntagAsync(
        LinkEntity entity,
        string entityId,
        string tagPath,
        CancellationToken ct = default)
    {
        var (table, idColumn) = TagJoinTable(entity);

        await using var exists = _conn.CreateCommand();
        exists.CommandText = $"""
            SELECT 1 FROM {table} jt
            JOIN tags t ON t.id = jt.tag_id
            WHERE jt.{idColumn} = @eid AND t.path = @path
            LIMIT 1
            """;
        exists.Bind("@eid", entityId);
        exists.Bind("@path", tagPath);
        var found = await exists.ExecuteScalarAsync(ct);
        if (found is null || found is DBNull) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            DELETE FROM {table}
            WHERE {idColumn} = @eid
              AND tag_id IN (SELECT id FROM tags WHERE path = @path)
            """;
        cmd.Bind("@eid", entityId);
        cmd.Bind("@path", tagPath);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>
    /// Deletes a tag and cascades to every entity that was tagged with it.
    /// </summary>
    public async Task<bool> DeleteTagAsync(string tagPath, CancellationToken ct = default)
    {
        await using var exists = _conn.CreateCommand();
        exists.CommandText = "SELECT 1 FROM tags WHERE path = @path LIMIT 1";
        exists.Bind("@path", tagPath);
        var found = await exists.ExecuteScalarAsync(ct);
        if (found is null || found is DBNull) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tags WHERE path = @path";
        cmd.Bind("@path", tagPath);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(
        LinkEntity entity,
        string entityId,
        CancellationToken ct = default)
    {
        var (table, idColumn) = TagJoinTable(entity);

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT t.id, t.path, t.description, t.created_at
            FROM tags t
            JOIN {table} jt ON jt.tag_id = t.id
            WHERE jt.{idColumn} = @eid
            ORDER BY t.path
            """;
        cmd.Bind("@eid", entityId);
        var results = new List<Tag>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            results.Add(new Tag(
                Id: rdr.GetStringSafe(0),
                Path: rdr.GetStringSafe(1),
                Description: rdr.GetStringOrNull(2),
                CreatedAtMs: rdr.GetInt64Safe(3)));
        }
        return results;
    }

    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, path, description, created_at
            FROM tags ORDER BY path
            """;
        var results = new List<Tag>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            results.Add(new Tag(
                Id: rdr.GetStringSafe(0),
                Path: rdr.GetStringSafe(1),
                Description: rdr.GetStringOrNull(2),
                CreatedAtMs: rdr.GetInt64Safe(3)));
        }
        return results;
    }

    // Table name + entity-id column name are hardcoded per entity because they
    // come from the schema (decision_tags / principle_tags / note_tags).
    // Building SQL via string interpolation is safe here: the only interpolated
    // values are compile-time constants derived from a validated enum.
    private static (string table, string idColumn) TagJoinTable(LinkEntity entity) => entity switch
    {
        LinkEntity.Decision => ("decision_tags", "decision_id"),
        LinkEntity.Principle => ("principle_tags", "principle_id"),
        LinkEntity.Note => ("note_tags", "note_id"),
        _ => throw new ArgumentOutOfRangeException(nameof(entity))
    };
}
