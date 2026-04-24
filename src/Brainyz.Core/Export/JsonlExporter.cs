// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Brainyz.Core.Errors;
using Brainyz.Core.Storage;
using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Export;

/// <summary>
/// Streams a <see cref="BrainStore"/> into a JSONL file in the topological
/// order mandated by §3.2 of the v0.3 spec. Memory footprint is O(1) in
/// the number of rows — each record is written to disk as it is read.
/// </summary>
public sealed class JsonlExporter
{
    private readonly BrainStore _store;
    private readonly string _brainyzVersion;

    public JsonlExporter(BrainStore store, string brainyzVersion)
    {
        _store = store;
        _brainyzVersion = brainyzVersion;
    }

    public async Task<ExportResult> ExportAsync(
        string outPath,
        string? projectFilter,
        bool includeHistory,
        CancellationToken ct = default)
    {
        // Validate --project if provided: the project must exist. Same error
        // the CLI surfaces via BZ_EXPORT_PROJECT_NOT_FOUND.
        if (projectFilter is not null)
        {
            if (await _store.GetProjectAsync(projectFilter, ct) is null)
                throw new BrainyzException(
                    ErrorCode.BZ_EXPORT_PROJECT_NOT_FOUND,
                    $"project '{projectFilter}' not found",
                    tip: "list available projects with `brainz list projects`");
        }

        FileStream stream;
        try
        {
            stream = new FileStream(outPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        }
        catch (Exception ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_EXPORT_WRITE_FAILED,
                $"cannot open '{outPath}' for writing: {ex.Message}",
                tip: "check the target directory exists and is writable",
                inner: ex);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            await using (stream)
            await using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { NewLine = "\n" })
            {
                // Pass 1: count rows cheaply (per spec §3, manifest counts are
                // authoritative). Then write the manifest with real counts.
                await CountAllAsync(counts, projectFilter, includeHistory, ct);

                await WriteRecordAsync(writer, new ExportManifest(
                    Type: "manifest",
                    FormatVersion: 1,
                    BrainyzVersion: _brainyzVersion,
                    SchemaVersion: SchemaVersion.Current,
                    ExportedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ProjectFilter: projectFilter,
                    IncludeHistory: includeHistory,
                    Counts: counts), ct);

                // Pass 2: data, in topological order (§3.2).
                await WriteProjectsAsync(writer, projectFilter, ct);
                await WriteProjectRemotesAsync(writer, projectFilter, ct);
                await WriteDecisionsAsync(writer, projectFilter, ct);
                await WriteAlternativesAsync(writer, projectFilter, ct);
                await WritePrinciplesAsync(writer, projectFilter, ct);
                await WriteNotesAsync(writer, projectFilter, ct);
                await WriteTagsAsync(writer, projectFilter, ct);
                await WriteTagLinksAsync(writer, projectFilter, ct);
                await WriteLinksAsync(writer, projectFilter, ct);
                await WriteEmbeddingsAsync(writer, projectFilter, ct);
                if (includeHistory)
                    await WriteHistoryAsync(writer, projectFilter, ct);
            }
        }
        catch (BrainyzException) { throw; }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new BrainyzException(
                ErrorCode.BZ_EXPORT_DISK_FULL,
                $"disk full while writing '{outPath}'",
                inner: ex);
        }
        catch (Exception ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_EXPORT_WRITE_FAILED,
                $"export to '{outPath}' failed: {ex.Message}",
                inner: ex);
        }

        return new ExportResult(OutPath: outPath, Counts: counts);
    }

    // ─────────── record writing ───────────

    private static async Task WriteRecordAsync<T>(StreamWriter writer, T record, CancellationToken ct)
        where T : notnull
    {
        var json = JsonSerializer.Serialize(record, typeof(T), BrainyzJsonlContext.Default);
        await writer.WriteLineAsync(json.AsMemory(), ct);
    }

    // ─────────── count pass ───────────

    private async Task CountAllAsync(
        Dictionary<string, int> counts, string? projectFilter, bool includeHistory,
        CancellationToken ct)
    {
        counts["project"]         = await CountAsync("projects", ProjectsWhere(projectFilter), projectFilter, ct);
        counts["project_remote"]  = await CountAsync("project_remotes", ProjectRemotesWhere(projectFilter), projectFilter, ct);
        counts["decision"]        = await CountAsync("decisions", ScopedProjectWhere("project_id", projectFilter), projectFilter, ct);
        counts["alternative"]     = await CountAsync("alternatives", AlternativesWhere(projectFilter), projectFilter, ct);
        counts["principle"]       = await CountAsync("principles", ScopedProjectWhere("project_id", projectFilter), projectFilter, ct);
        counts["note"]            = await CountAsync("notes", ScopedProjectWhere("project_id", projectFilter), projectFilter, ct);
        counts["tag"]             = await CountAsync("tags", TagsWhere(projectFilter), projectFilter, ct);
        counts["tag_link"]        = await CountTagLinksAsync(projectFilter, ct);
        counts["link"]            = await CountAsync("links", LinksWhere(projectFilter), projectFilter, ct);
        counts["embedding"]       = await CountEmbeddingsAsync(projectFilter, ct);
        counts["history_entry"]   = includeHistory ? await CountHistoryAsync(projectFilter, ct) : 0;
    }

    private async Task<int> CountAsync(string table, string where, string? projectFilter, CancellationToken ct)
    {
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = string.IsNullOrEmpty(where)
            ? $"SELECT COUNT(*) FROM {table}"
            : $"SELECT COUNT(*) FROM {table} {where}";
        if (projectFilter is not null && where.Contains("@p"))
            cmd.Bind("@p", projectFilter);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private async Task<int> CountTagLinksAsync(string? projectFilter, CancellationToken ct)
    {
        // Sum of the three tag-link tables, scoped to the project when set.
        var sql = projectFilter is null
            ? "SELECT (SELECT COUNT(*) FROM decision_tags) " +
              "     + (SELECT COUNT(*) FROM principle_tags) " +
              "     + (SELECT COUNT(*) FROM note_tags)"
            : "SELECT (SELECT COUNT(*) FROM decision_tags WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)) " +
              "     + (SELECT COUNT(*) FROM principle_tags WHERE principle_id IN (SELECT id FROM principles WHERE project_id = @p)) " +
              "     + (SELECT COUNT(*) FROM note_tags WHERE note_id IN (SELECT id FROM notes WHERE project_id = @p))";

        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (projectFilter is not null) cmd.Bind("@p", projectFilter);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private async Task<int> CountEmbeddingsAsync(string? projectFilter, CancellationToken ct)
    {
        var sql = projectFilter is null
            ? "SELECT (SELECT COUNT(*) FROM decision_embeddings) " +
              "     + (SELECT COUNT(*) FROM principle_embeddings) " +
              "     + (SELECT COUNT(*) FROM note_embeddings)"
            : "SELECT (SELECT COUNT(*) FROM decision_embeddings WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)) " +
              "     + (SELECT COUNT(*) FROM principle_embeddings WHERE principle_id IN (SELECT id FROM principles WHERE project_id = @p)) " +
              "     + (SELECT COUNT(*) FROM note_embeddings WHERE note_id IN (SELECT id FROM notes WHERE project_id = @p))";

        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (projectFilter is not null) cmd.Bind("@p", projectFilter);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private async Task<int> CountHistoryAsync(string? projectFilter, CancellationToken ct)
    {
        // History tables keep the historical project_id on the snapshot row
        // itself, so we can filter directly.
        var sql = projectFilter is null
            ? "SELECT (SELECT COUNT(*) FROM decisions_history) " +
              "     + (SELECT COUNT(*) FROM principles_history) " +
              "     + (SELECT COUNT(*) FROM notes_history)"
            : "SELECT (SELECT COUNT(*) FROM decisions_history WHERE project_id = @p) " +
              "     + (SELECT COUNT(*) FROM principles_history WHERE project_id = @p) " +
              "     + (SELECT COUNT(*) FROM notes_history WHERE project_id = @p)";

        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (projectFilter is not null) cmd.Bind("@p", projectFilter);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    // ─────────── WHERE-clause helpers ───────────

    private static string ProjectsWhere(string? p) => p is null ? "" : "WHERE id = @p";
    private static string ProjectRemotesWhere(string? p) => p is null ? "" : "WHERE project_id = @p";
    private static string ScopedProjectWhere(string col, string? p) => p is null ? "" : $"WHERE {col} = @p";
    private static string AlternativesWhere(string? p) =>
        p is null ? "" : "WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)";
    private static string TagsWhere(string? p) => p is null ? "" : """
        WHERE id IN (
            SELECT tag_id FROM decision_tags WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)
            UNION
            SELECT tag_id FROM principle_tags WHERE principle_id IN (SELECT id FROM principles WHERE project_id = @p)
            UNION
            SELECT tag_id FROM note_tags WHERE note_id IN (SELECT id FROM notes WHERE project_id = @p)
        )
        """;
    private static string LinksWhere(string? p)
    {
        if (p is null) return "";
        // Both endpoints must belong to the filtered project. A link is in
        // scope only if every referenced entity belongs to the project.
        return """
            WHERE from_id IN (
                SELECT id FROM decisions  WHERE project_id = @p
                UNION SELECT id FROM principles WHERE project_id = @p
                UNION SELECT id FROM notes      WHERE project_id = @p
            ) AND to_id IN (
                SELECT id FROM decisions  WHERE project_id = @p
                UNION SELECT id FROM principles WHERE project_id = @p
                UNION SELECT id FROM notes      WHERE project_id = @p
            )
            """;
    }

    // ─────────── entity writers ───────────

    private async Task WriteProjectsAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, slug, name, description, created_at, updated_at FROM projects {ProjectsWhere(p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlProjectRecord(
                Type: "project",
                Id: rdr.GetString(0),
                Slug: rdr.GetString(1),
                Name: rdr.GetString(2),
                Description: rdr.IsDBNull(3) ? null : rdr.GetString(3),
                CreatedAtMs: rdr.GetInt64(4),
                UpdatedAtMs: rdr.GetInt64(5)), ct);
        }
    }

    private async Task WriteProjectRemotesAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, project_id, remote_url, role, created_at FROM project_remotes {ProjectRemotesWhere(p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlProjectRemoteRecord(
                Type: "project_remote",
                Id: rdr.GetString(0),
                ProjectId: rdr.GetString(1),
                RemoteUrl: rdr.GetString(2),
                Role: rdr.GetString(3),
                CreatedAtMs: rdr.GetInt64(4)), ct);
        }
    }

    private async Task WriteDecisionsAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, project_id, title, status, confidence, context, decision, rationale, consequences, revisit_at, created_at, updated_at " +
                  $"FROM decisions {ScopedProjectWhere("project_id", p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlDecisionRecord(
                Type: "decision",
                Id: rdr.GetString(0),
                ProjectId: rdr.IsDBNull(1) ? null : rdr.GetString(1),
                Title: rdr.GetString(2),
                Status: rdr.GetString(3),
                Confidence: rdr.IsDBNull(4) ? null : rdr.GetString(4),
                Context: rdr.IsDBNull(5) ? null : rdr.GetString(5),
                Decision: rdr.GetString(6),
                Rationale: rdr.IsDBNull(7) ? null : rdr.GetString(7),
                Consequences: rdr.IsDBNull(8) ? null : rdr.GetString(8),
                RevisitAtMs: rdr.IsDBNull(9) ? null : rdr.GetInt64(9),
                CreatedAtMs: rdr.GetInt64(10),
                UpdatedAtMs: rdr.GetInt64(11)), ct);
        }
    }

    private async Task WriteAlternativesAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, decision_id, title, description, why_rejected, sort_order, created_at " +
                  $"FROM alternatives {AlternativesWhere(p)} ORDER BY decision_id, sort_order";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlAlternativeRecord(
                Type: "alternative",
                Id: rdr.GetString(0),
                DecisionId: rdr.GetString(1),
                Title: rdr.GetString(2),
                Description: rdr.IsDBNull(3) ? null : rdr.GetString(3),
                WhyRejected: rdr.GetString(4),
                SortOrder: rdr.GetInt32(5),
                CreatedAtMs: rdr.GetInt64(6)), ct);
        }
    }

    private async Task WritePrinciplesAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, project_id, title, statement, rationale, active, created_at, updated_at " +
                  $"FROM principles {ScopedProjectWhere("project_id", p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlPrincipleRecord(
                Type: "principle",
                Id: rdr.GetString(0),
                ProjectId: rdr.IsDBNull(1) ? null : rdr.GetString(1),
                Title: rdr.GetString(2),
                Statement: rdr.GetString(3),
                Rationale: rdr.IsDBNull(4) ? null : rdr.GetString(4),
                Active: rdr.GetInt32(5),
                CreatedAtMs: rdr.GetInt64(6),
                UpdatedAtMs: rdr.GetInt64(7)), ct);
        }
    }

    private async Task WriteNotesAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, project_id, title, content, source, active, created_at, updated_at " +
                  $"FROM notes {ScopedProjectWhere("project_id", p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlNoteRecord(
                Type: "note",
                Id: rdr.GetString(0),
                ProjectId: rdr.IsDBNull(1) ? null : rdr.GetString(1),
                Title: rdr.GetString(2),
                Content: rdr.GetString(3),
                Source: rdr.IsDBNull(4) ? null : rdr.GetString(4),
                Active: rdr.GetInt32(5),
                CreatedAtMs: rdr.GetInt64(6),
                UpdatedAtMs: rdr.GetInt64(7)), ct);
        }
    }

    private async Task WriteTagsAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, path, description, created_at FROM tags {TagsWhere(p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlTagRecord(
                Type: "tag",
                Id: rdr.GetString(0),
                Path: rdr.GetString(1),
                Description: rdr.IsDBNull(2) ? null : rdr.GetString(2),
                CreatedAtMs: rdr.GetInt64(3)), ct);
        }
    }

    private async Task WriteTagLinksAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = p is null
            ? """
                SELECT 'decision'  AS entity_type, decision_id  AS entity_id, tag_id FROM decision_tags
                UNION ALL
                SELECT 'principle', principle_id, tag_id FROM principle_tags
                UNION ALL
                SELECT 'note',      note_id,      tag_id FROM note_tags
                """
            : """
                SELECT 'decision'  AS entity_type, decision_id  AS entity_id, tag_id FROM decision_tags
                  WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)
                UNION ALL
                SELECT 'principle', principle_id, tag_id FROM principle_tags
                  WHERE principle_id IN (SELECT id FROM principles WHERE project_id = @p)
                UNION ALL
                SELECT 'note',      note_id,      tag_id FROM note_tags
                  WHERE note_id IN (SELECT id FROM notes WHERE project_id = @p)
                """;
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlTagLinkRecord(
                Type: "tag_link",
                EntityType: rdr.GetString(0),
                EntityId: rdr.GetString(1),
                TagId: rdr.GetString(2)), ct);
        }
    }

    private async Task WriteLinksAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = $"SELECT id, from_type, from_id, to_type, to_id, relation_type, note, created_at " +
                  $"FROM links {LinksWhere(p)} ORDER BY created_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            await WriteRecordAsync(writer, new JsonlLinkRecord(
                Type: "link",
                Id: rdr.GetString(0),
                FromType: rdr.GetString(1),
                FromId: rdr.GetString(2),
                ToType: rdr.GetString(3),
                ToId: rdr.GetString(4),
                RelationType: rdr.GetString(5),
                Note: rdr.IsDBNull(6) ? null : rdr.GetString(6),
                CreatedAtMs: rdr.GetInt64(7)), ct);
        }
    }

    private async Task WriteEmbeddingsAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var baseSql = """
            SELECT 'decision'  AS entity_type, decision_id  AS entity_id, model, dim, vector, content_hash, created_at FROM decision_embeddings
            UNION ALL
            SELECT 'principle', principle_id, model, dim, vector, content_hash, created_at FROM principle_embeddings
            UNION ALL
            SELECT 'note',      note_id,      model, dim, vector, content_hash, created_at FROM note_embeddings
            """;

        // Project filter needs per-subquery WHEREs.
        var sql = p is null ? baseSql : """
            SELECT 'decision'  AS entity_type, decision_id  AS entity_id, model, dim, vector, content_hash, created_at FROM decision_embeddings
              WHERE decision_id IN (SELECT id FROM decisions WHERE project_id = @p)
            UNION ALL
            SELECT 'principle', principle_id, model, dim, vector, content_hash, created_at FROM principle_embeddings
              WHERE principle_id IN (SELECT id FROM principles WHERE project_id = @p)
            UNION ALL
            SELECT 'note',      note_id,      model, dim, vector, content_hash, created_at FROM note_embeddings
              WHERE note_id IN (SELECT id FROM notes WHERE project_id = @p)
            """;

        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var entityType = rdr.GetString(0);
            var entityId   = rdr.GetString(1);
            var model      = rdr.GetString(2);
            var dim        = rdr.GetInt32(3);
            // F32_BLOB read as byte[] — little-endian on every RID we ship.
            var blob       = (byte[])rdr.GetValue(4);
            var hash       = rdr.GetString(5);
            var createdAt  = rdr.GetInt64(6);

            await WriteRecordAsync(writer, new JsonlEmbeddingRecord(
                Type: "embedding",
                EntityType: entityType,
                EntityId: entityId,
                Model: model,
                Dim: dim,
                ContentHash: hash,
                VectorBase64: Convert.ToBase64String(blob),
                CreatedAtMs: createdAt), ct);
        }
    }

    private async Task WriteHistoryAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        await WriteDecisionsHistoryAsync(writer, p, ct);
        await WritePrinciplesHistoryAsync(writer, p, ct);
        await WriteNotesHistoryAsync(writer, p, ct);
    }

    private async Task WriteDecisionsHistoryAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = p is null
            ? "SELECT history_id, decision_id, change_type, changed_at, project_id, title, status, confidence, context, decision, rationale, consequences, revisit_at FROM decisions_history ORDER BY changed_at"
            : "SELECT history_id, decision_id, change_type, changed_at, project_id, title, status, confidence, context, decision, rationale, consequences, revisit_at FROM decisions_history WHERE project_id = @p ORDER BY changed_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_id"]   = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                ["title"]        = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                ["status"]       = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                ["confidence"]   = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                ["context"]      = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                ["decision"]     = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                ["rationale"]    = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                ["consequences"] = rdr.IsDBNull(11) ? null : rdr.GetString(11),
                ["revisit_at"]   = rdr.IsDBNull(12) ? null : rdr.GetInt64(12),
            };
            await WriteRecordAsync(writer, new JsonlHistoryRecord(
                Type: "history_entry",
                EntityType: "decision",
                EntityId: rdr.GetString(1),
                ChangeType: rdr.GetString(2),
                ChangedAtMs: rdr.GetInt64(3),
                Snapshot: snapshot), ct);
        }
    }

    private async Task WritePrinciplesHistoryAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = p is null
            ? "SELECT history_id, principle_id, change_type, changed_at, project_id, title, statement, rationale, active FROM principles_history ORDER BY changed_at"
            : "SELECT history_id, principle_id, change_type, changed_at, project_id, title, statement, rationale, active FROM principles_history WHERE project_id = @p ORDER BY changed_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_id"] = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                ["title"]      = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                ["statement"]  = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                ["rationale"]  = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                ["active"]     = rdr.IsDBNull(8) ? null : rdr.GetInt32(8),
            };
            await WriteRecordAsync(writer, new JsonlHistoryRecord(
                Type: "history_entry",
                EntityType: "principle",
                EntityId: rdr.GetString(1),
                ChangeType: rdr.GetString(2),
                ChangedAtMs: rdr.GetInt64(3),
                Snapshot: snapshot), ct);
        }
    }

    private async Task WriteNotesHistoryAsync(StreamWriter writer, string? p, CancellationToken ct)
    {
        var sql = p is null
            ? "SELECT history_id, note_id, change_type, changed_at, project_id, title, content, source, active FROM notes_history ORDER BY changed_at"
            : "SELECT history_id, note_id, change_type, changed_at, project_id, title, content, source, active FROM notes_history WHERE project_id = @p ORDER BY changed_at";
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        if (p is not null) cmd.Bind("@p", p);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["project_id"] = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                ["title"]      = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                ["content"]    = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                ["source"]     = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                ["active"]     = rdr.IsDBNull(8) ? null : rdr.GetInt32(8),
            };
            await WriteRecordAsync(writer, new JsonlHistoryRecord(
                Type: "history_entry",
                EntityType: "note",
                EntityId: rdr.GetString(1),
                ChangeType: rdr.GetString(2),
                ChangedAtMs: rdr.GetInt64(3),
                Snapshot: snapshot), ct);
        }
    }

    private static bool IsDiskFull(Exception ex) =>
        ex is IOException io &&
        (io.HResult == unchecked((int)0x80070070) /* ERROR_DISK_FULL */
         || io.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase));
}

public sealed record ExportResult(string OutPath, Dictionary<string, int> Counts);
