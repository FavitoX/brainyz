// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using System.Runtime.InteropServices;
using System.Text.Json;
using Brainyz.Core.Errors;
using Brainyz.Core.Safety;
using Brainyz.Core.Storage;
using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Import;

/// <summary>
/// Parses a JSONL export and restores it into a <see cref="BrainStore"/>.
/// See §4 of the v0.3 design spec for the two-mode semantics.
/// </summary>
public sealed class JsonlImporter
{
    private readonly BrainStore _store;
    private readonly string _brainyzVersion;

    public JsonlImporter(BrainStore store, string brainyzVersion)
    {
        _store = store;
        _brainyzVersion = brainyzVersion;
    }

    public async Task<ImportResult> ImportAsync(
        string path,
        ImportMode mode,
        bool skipSafetyBackup = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new BrainyzException(
                ErrorCode.BZ_IMPORT_FILE_NOT_FOUND,
                $"import file '{path}' not found");

        await using var file = File.OpenRead(path);
        using var reader = new StreamReader(file, System.Text.Encoding.UTF8);

        // ── 1. Manifest validation ──────────────────────────────────────────
        var firstLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(firstLine))
            throw new BrainyzException(
                ErrorCode.BZ_IMPORT_MANIFEST_MISSING,
                "import file is empty or first line is blank");

        JsonDocument manifestDoc;
        try { manifestDoc = JsonDocument.Parse(firstLine); }
        catch (JsonException ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON,
                $"first line is not valid JSON: {ex.Message}",
                inner: ex);
        }

        JsonElement manifestRoot = manifestDoc.RootElement;
        try
        {
            if (!manifestRoot.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "manifest")
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_MANIFEST_MISSING,
                    "first line is not a manifest record (expected type = \"manifest\")");

            int fmt = manifestRoot.TryGetProperty("format_version", out var fv) && fv.ValueKind == JsonValueKind.Number
                ? fv.GetInt32()
                : throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON,
                    "manifest.format_version missing or not a number");

            if (fmt > 1)
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_FORMAT_VERSION_NEWER,
                    $"export was created for format version {fmt}; this brainyz supports up to version 1",
                    tip: "upgrade brainyz to the version that wrote the export, or re-export from a matching installation");

            int sch = manifestRoot.TryGetProperty("schema_version", out var sv) && sv.ValueKind == JsonValueKind.Number
                ? sv.GetInt32()
                : throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON,
                    "manifest.schema_version missing or not a number");

            if (sch != SchemaVersion.Current)
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH,
                    $"export was created for schema version {sch}, local DB is on version {SchemaVersion.Current}",
                    tip: "migration is not yet supported; re-export from an installation matching the local schema version");

            // Clone the manifest element now — we need it after manifestDoc is
            // disposed for integrity checks.
            manifestRoot = manifestRoot.Clone();
        }
        finally { manifestDoc.Dispose(); }

        var inserted = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped  = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();

        // ── 2. Dry run exits here after successfully parsing every line ─────
        if (dryRun)
        {
            await ParseAllRecordsForValidationAsync(reader, inserted, ct);
            return new ImportResult(mode, inserted, skipped, warnings,
                RowCountMismatch: false, SafetyBackupPath: null);
        }

        // ── 3. Safety backup (always unless --no-backup) ────────────────────
        string? safetyBackupPath = null;
        if (!skipSafetyBackup && File.Exists(_store.DbPath))
        {
            var res = await SafetyBackup.CreateAsync(
                _store.DbPath,
                operation: "import",
                failureCode: ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED,
                ct);
            safetyBackupPath = res.BackupPath;
        }

        // ── 4. Mutation transaction ─────────────────────────────────────────
        await using var tx = await _store.Connection.BeginTransactionAsync(ct);
        try
        {
            if (mode == ImportMode.Replace)
                await ClearAllTablesAsync(tx, ct);

            await StreamRecordsAsync(reader, tx, mode, inserted, skipped, ct);

            await tx.CommitAsync(ct);
        }
        catch (BrainyzException) { await tx.RollbackAsync(ct); throw; }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            await tx.RollbackAsync(ct);
            throw new BrainyzException(
                ErrorCode.BZ_IMPORT_DISK_FULL,
                $"disk full during import: {ex.Message}", inner: ex);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            var msg = ex.Message ?? "";
            var code = msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                ? ErrorCode.BZ_IMPORT_FK_VIOLATION
                : ErrorCode.BZ_IMPORT_TRANSACTION_FAILED;
            throw new BrainyzException(code,
                $"import transaction rolled back: {ex.Message}", inner: ex);
        }

        // ── 5. FTS rebuild (second, smaller transaction; warning-only on fail)
        await TryRebuildFtsAsync(warnings, ct);

        // ── 6. Integrity check (§4.4) ───────────────────────────────────────
        var rowCountMismatch = await IntegrityCheckAsync(manifestRoot, mode, warnings, ct);

        return new ImportResult(mode, inserted, skipped, warnings,
            RowCountMismatch: rowCountMismatch,
            SafetyBackupPath: safetyBackupPath);
    }

    // ─────────── Dry-run parse (no mutation) ───────────

    private async Task ParseAllRecordsForValidationAsync(
        StreamReader reader, Dictionary<string, int> counts, CancellationToken ct)
    {
        string? line;
        int lineNum = 1;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException ex)
            {
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                    $"line {lineNum}: invalid JSON ({ex.Message})", inner: ex);
            }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("type", out var t) || t.GetString() is null)
                    throw new BrainyzException(
                        ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                        $"line {lineNum}: missing \"type\" discriminator");
                var type = t.GetString()!;
                counts[type] = counts.TryGetValue(type, out var v) ? v + 1 : 1;
            }
        }
    }

    // ─────────── Clear tables (replace mode only) ───────────

    private static async Task ClearAllTablesAsync(DbTransaction tx, CancellationToken ct)
    {
        // Reverse of §3.2 insert order, plus the _history tables which are
        // logically first (they reference the entities) but in practice
        // have no FK constraints — they keep their own project_id snapshot.
        string[] tables =
        [
            "decisions_history", "principles_history", "notes_history",
            "decision_embeddings", "principle_embeddings", "note_embeddings",
            "links",
            "decision_tags", "principle_tags", "note_tags",
            "tags",
            "alternatives",
            "notes", "principles", "decisions",
            "project_remotes", "projects",
        ];
        foreach (var t in tables)
        {
            await using var cmd = tx.Connection!.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {t}";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // ─────────── Stream records → route to INSERT ───────────

    private static async Task StreamRecordsAsync(
        StreamReader reader,
        DbTransaction tx,
        ImportMode mode,
        Dictionary<string, int> inserted,
        Dictionary<string, int> skipped,
        CancellationToken ct)
    {
        string? line;
        int lineNum = 1; // manifest was line 1; subsequent lines start at 2
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException ex)
            {
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                    $"line {lineNum}: invalid JSON ({ex.Message})", inner: ex);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() is null)
                    throw new BrainyzException(
                        ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                        $"line {lineNum}: missing \"type\" discriminator");

                var type = typeEl.GetString()!;
                try
                {
                    bool inserted_ = type switch
                    {
                        "project"        => await InsertProjectAsync(tx, root, mode, ct),
                        "project_remote" => await InsertProjectRemoteAsync(tx, root, mode, ct),
                        "decision"       => await InsertDecisionAsync(tx, root, mode, ct),
                        "alternative"    => await InsertAlternativeAsync(tx, root, mode, ct),
                        "principle"      => await InsertPrincipleAsync(tx, root, mode, ct),
                        "note"           => await InsertNoteAsync(tx, root, mode, ct),
                        "tag"            => await InsertTagAsync(tx, root, mode, ct),
                        "tag_link"       => await InsertTagLinkAsync(tx, root, mode, ct),
                        "link"           => await InsertLinkAsync(tx, root, mode, ct),
                        "embedding"      => await InsertEmbeddingAsync(tx, root, mode, ct),
                        "history_entry"  => await InsertHistoryAsync(tx, root, ct),
                        "manifest"       => throw new BrainyzException(
                                                ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                                                $"line {lineNum}: unexpected second manifest record"),
                        _ => true, // forward-compat: unknown type skipped silently
                    };
                    if (inserted_) Bump(inserted, type);
                    else           Bump(skipped, type);
                }
                catch (BrainyzException) { throw; }
                catch (Exception ex)
                {
                    var msg = ex.Message ?? "";
                    var code = msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                        ? ErrorCode.BZ_IMPORT_FK_VIOLATION
                        : ErrorCode.BZ_IMPORT_TRANSACTION_FAILED;
                    throw new BrainyzException(code,
                        $"line {lineNum} ({type}): {ex.Message}", inner: ex);
                }
            }
        }

        static void Bump(Dictionary<string, int> d, string k) =>
            d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;
    }

    // ─────────── Per-type INSERT helpers ───────────
    //
    // Each returns true when the row was INSERTed, false when skipped (only
    // possible in additive mode on PK collision). `Bind` calls are in SQL-
    // placeholder order — Nelknet 0.2.5 binds positionally (see nelknet-bugs.md).

    private static async Task<bool> InsertProjectAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "projects", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO projects (id, slug, name, description, created_at, updated_at)
            VALUES (@id, @slug, @name, @desc, @ca, @ua)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@slug", el.GetProperty("slug").GetString());
        cmd.Bind("@name", el.GetProperty("name").GetString());
        cmd.Bind("@desc", GetNullableString(el, "description"));
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        cmd.Bind("@ua", el.GetProperty("updated_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertProjectRemoteAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "project_remotes", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO project_remotes (id, project_id, remote_url, role, created_at)
            VALUES (@id, @pid, @url, @role, @ca)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@pid", el.GetProperty("project_id").GetString());
        cmd.Bind("@url", el.GetProperty("remote_url").GetString());
        cmd.Bind("@role", el.GetProperty("role").GetString());
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertDecisionAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "decisions", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO decisions
                (id, project_id, title, status, confidence,
                 context, decision, rationale, consequences,
                 revisit_at, created_at, updated_at)
            VALUES (@id, @pid, @title, @status, @conf,
                    @ctx, @dec, @rat, @cons,
                    @revisit, @ca, @ua)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@pid", GetNullableString(el, "project_id"));
        cmd.Bind("@title", el.GetProperty("title").GetString());
        cmd.Bind("@status", el.GetProperty("status").GetString());
        cmd.Bind("@conf", GetNullableString(el, "confidence"));
        cmd.Bind("@ctx", GetNullableString(el, "context"));
        cmd.Bind("@dec", el.GetProperty("decision").GetString());
        cmd.Bind("@rat", GetNullableString(el, "rationale"));
        cmd.Bind("@cons", GetNullableString(el, "consequences"));
        cmd.Bind("@revisit", (object?)GetNullableInt64(el, "revisit_at_ms"));
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        cmd.Bind("@ua", el.GetProperty("updated_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertAlternativeAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "alternatives", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO alternatives (id, decision_id, title, description, why_rejected, sort_order, created_at)
            VALUES (@id, @did, @title, @desc, @why, @sort, @ca)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@did", el.GetProperty("decision_id").GetString());
        cmd.Bind("@title", el.GetProperty("title").GetString());
        cmd.Bind("@desc", GetNullableString(el, "description"));
        cmd.Bind("@why", el.GetProperty("why_rejected").GetString());
        cmd.Bind("@sort", el.GetProperty("sort_order").GetInt32());
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertPrincipleAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "principles", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO principles (id, project_id, title, statement, rationale, active, created_at, updated_at)
            VALUES (@id, @pid, @title, @st, @rat, @active, @ca, @ua)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@pid", GetNullableString(el, "project_id"));
        cmd.Bind("@title", el.GetProperty("title").GetString());
        cmd.Bind("@st", el.GetProperty("statement").GetString());
        cmd.Bind("@rat", GetNullableString(el, "rationale"));
        cmd.Bind("@active", el.GetProperty("active").GetInt32());
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        cmd.Bind("@ua", el.GetProperty("updated_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertNoteAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "notes", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO notes (id, project_id, title, content, source, active, created_at, updated_at)
            VALUES (@id, @pid, @title, @content, @source, @active, @ca, @ua)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@pid", GetNullableString(el, "project_id"));
        cmd.Bind("@title", el.GetProperty("title").GetString());
        cmd.Bind("@content", el.GetProperty("content").GetString());
        cmd.Bind("@source", GetNullableString(el, "source"));
        cmd.Bind("@active", el.GetProperty("active").GetInt32());
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        cmd.Bind("@ua", el.GetProperty("updated_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertTagAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "tags", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO tags (id, path, description, created_at)
            VALUES (@id, @path, @desc, @ca)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@path", el.GetProperty("path").GetString());
        cmd.Bind("@desc", GetNullableString(el, "description"));
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertTagLinkAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var entityType = el.GetProperty("entity_type").GetString()!;
        var entityId = el.GetProperty("entity_id").GetString()!;
        var tagId = el.GetProperty("tag_id").GetString()!;

        var (table, pkCol) = entityType switch
        {
            "decision"  => ("decision_tags", "decision_id"),
            "principle" => ("principle_tags", "principle_id"),
            "note"      => ("note_tags", "note_id"),
            _ => throw new BrainyzException(
                ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                $"tag_link: unknown entity_type '{entityType}'")
        };

        if (mode == ImportMode.Additive)
        {
            await using var probe = tx.Connection!.CreateCommand();
            probe.Transaction = tx;
            probe.CommandText = $"SELECT 1 FROM {table} WHERE {pkCol} = @e AND tag_id = @t LIMIT 1";
            probe.Bind("@e", entityId);
            probe.Bind("@t", tagId);
            if (await probe.ExecuteScalarAsync(ct) is not null) return false;
        }

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {table} ({pkCol}, tag_id) VALUES (@e, @t)";
        cmd.Bind("@e", entityId);
        cmd.Bind("@t", tagId);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertLinkAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var id = el.GetProperty("id").GetString()!;
        if (mode == ImportMode.Additive && await ExistsAsync(tx, "links", "id", id, ct)) return false;

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO links (id, from_type, from_id, to_type, to_id, relation_type, note, created_at)
            VALUES (@id, @ft, @fi, @tt, @ti, @rel, @note, @ca)
            """;
        cmd.Bind("@id", id);
        cmd.Bind("@ft", el.GetProperty("from_type").GetString());
        cmd.Bind("@fi", el.GetProperty("from_id").GetString());
        cmd.Bind("@tt", el.GetProperty("to_type").GetString());
        cmd.Bind("@ti", el.GetProperty("to_id").GetString());
        cmd.Bind("@rel", el.GetProperty("relation_type").GetString());
        cmd.Bind("@note", GetNullableString(el, "note"));
        cmd.Bind("@ca", el.GetProperty("created_at_ms").GetInt64());
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertEmbeddingAsync(DbTransaction tx, JsonElement el, ImportMode mode, CancellationToken ct)
    {
        var entityType = el.GetProperty("entity_type").GetString()!;
        var entityId = el.GetProperty("entity_id").GetString()!;
        var model = el.GetProperty("model").GetString()!;
        var dim = el.GetProperty("dim").GetInt32();
        var contentHash = el.GetProperty("content_hash").GetString()!;
        var createdAt = el.GetProperty("created_at_ms").GetInt64();
        var base64 = el.GetProperty("vector_base64").GetString()!;

        var (table, pkCol) = entityType switch
        {
            "decision"  => ("decision_embeddings", "decision_id"),
            "principle" => ("principle_embeddings", "principle_id"),
            "note"      => ("note_embeddings", "note_id"),
            _ => throw new BrainyzException(
                ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                $"embedding: unknown entity_type '{entityType}'")
        };

        if (mode == ImportMode.Additive)
        {
            await using var probe = tx.Connection!.CreateCommand();
            probe.Transaction = tx;
            probe.CommandText = $"SELECT 1 FROM {table} WHERE {pkCol} = @e AND model = @m LIMIT 1";
            probe.Bind("@e", entityId);
            probe.Bind("@m", model);
            if (await probe.ExecuteScalarAsync(ct) is not null) return false;
        }

        var bytes = Convert.FromBase64String(base64);
        var floats = MemoryMarshal.Cast<byte, float>(bytes);
        var literal = BrainStore.VectorLiteral(floats);

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO {table} ({pkCol}, model, dim, vector, content_hash, created_at)
            VALUES (@e, @m, @d, vector32(@vec), @h, @ca)
            """;
        cmd.Bind("@e", entityId);
        cmd.Bind("@m", model);
        cmd.Bind("@d", dim);
        cmd.Bind("@vec", literal);
        cmd.Bind("@h", contentHash);
        cmd.Bind("@ca", createdAt);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task<bool> InsertHistoryAsync(DbTransaction tx, JsonElement el, CancellationToken ct)
    {
        // history_id is AUTOINCREMENT; we let the target DB assign a new one,
        // so additive deduplication by PK is not possible here. We always
        // insert; callers who re-import the same export twice will duplicate
        // history rows. Acceptable for v0.3 since history is append-only and
        // rarely critical; revisit when a proper sync path lands.
        var entityType = el.GetProperty("entity_type").GetString()!;
        var entityId = el.GetProperty("entity_id").GetString()!;
        var changeType = el.GetProperty("change_type").GetString()!;
        var changedAt = el.GetProperty("changed_at_ms").GetInt64();
        var snapshot = el.GetProperty("snapshot");

        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;

        switch (entityType)
        {
            case "decision":
                cmd.CommandText = """
                    INSERT INTO decisions_history
                        (decision_id, change_type, changed_at, project_id, title, status, confidence,
                         context, decision, rationale, consequences, revisit_at)
                    VALUES (@id, @ct_, @at,
                            @pid, @title, @status, @conf,
                            @ctx, @dec, @rat, @cons, @rev)
                    """;
                cmd.Bind("@id", entityId);
                cmd.Bind("@ct_", changeType);
                cmd.Bind("@at", changedAt);
                cmd.Bind("@pid", GetSnap(snapshot, "project_id"));
                cmd.Bind("@title", GetSnap(snapshot, "title"));
                cmd.Bind("@status", GetSnap(snapshot, "status"));
                cmd.Bind("@conf", GetSnap(snapshot, "confidence"));
                cmd.Bind("@ctx", GetSnap(snapshot, "context"));
                cmd.Bind("@dec", GetSnap(snapshot, "decision"));
                cmd.Bind("@rat", GetSnap(snapshot, "rationale"));
                cmd.Bind("@cons", GetSnap(snapshot, "consequences"));
                cmd.Bind("@rev", GetSnapLong(snapshot, "revisit_at"));
                break;

            case "principle":
                cmd.CommandText = """
                    INSERT INTO principles_history
                        (principle_id, change_type, changed_at, project_id, title, statement, rationale, active)
                    VALUES (@id, @ct_, @at, @pid, @title, @st, @rat, @active)
                    """;
                cmd.Bind("@id", entityId);
                cmd.Bind("@ct_", changeType);
                cmd.Bind("@at", changedAt);
                cmd.Bind("@pid", GetSnap(snapshot, "project_id"));
                cmd.Bind("@title", GetSnap(snapshot, "title"));
                cmd.Bind("@st", GetSnap(snapshot, "statement"));
                cmd.Bind("@rat", GetSnap(snapshot, "rationale"));
                cmd.Bind("@active", GetSnapLong(snapshot, "active"));
                break;

            case "note":
                cmd.CommandText = """
                    INSERT INTO notes_history
                        (note_id, change_type, changed_at, project_id, title, content, source, active)
                    VALUES (@id, @ct_, @at, @pid, @title, @content, @source, @active)
                    """;
                cmd.Bind("@id", entityId);
                cmd.Bind("@ct_", changeType);
                cmd.Bind("@at", changedAt);
                cmd.Bind("@pid", GetSnap(snapshot, "project_id"));
                cmd.Bind("@title", GetSnap(snapshot, "title"));
                cmd.Bind("@content", GetSnap(snapshot, "content"));
                cmd.Bind("@source", GetSnap(snapshot, "source"));
                cmd.Bind("@active", GetSnapLong(snapshot, "active"));
                break;

            default:
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                    $"history_entry: unknown entity_type '{entityType}'");
        }

        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    // ─────────── Small helpers ───────────

    private static async Task<bool> ExistsAsync(
        DbTransaction tx, string table, string pkCol, string id, CancellationToken ct)
    {
        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT 1 FROM {table} WHERE {pkCol} = @id LIMIT 1";
        cmd.Bind("@id", id);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static string? GetNullableString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString()
            : null;

    private static long? GetNullableInt64(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : null;

    private static object? GetSnap(JsonElement snapshot, string prop)
    {
        if (!snapshot.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.TryGetInt64(out var i) ? (object)i : v.GetDouble(),
            JsonValueKind.True => 1L,
            JsonValueKind.False => 0L,
            _ => v.GetRawText(),
        };
    }

    private static object? GetSnapLong(JsonElement snapshot, string prop)
    {
        if (!snapshot.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    }

    // ─────────── FTS rebuild ───────────

    private async Task TryRebuildFtsAsync(List<string> warnings, CancellationToken ct)
    {
        string[] rebuildStatements =
        [
            "DELETE FROM decisions_fts",
            "INSERT INTO decisions_fts (decision_id, title, context, decision, rationale) " +
                "SELECT id, title, context, decision, rationale FROM decisions",
            "DELETE FROM principles_fts",
            "INSERT INTO principles_fts (principle_id, title, statement, rationale) " +
                "SELECT id, title, statement, rationale FROM principles",
            "DELETE FROM notes_fts",
            "INSERT INTO notes_fts (note_id, title, content, source) " +
                "SELECT id, title, content, source FROM notes",
        ];
        try
        {
            await using var tx = await _store.Connection.BeginTransactionAsync(ct);
            foreach (var sql in rebuildStatements)
            {
                await using var cmd = tx.Connection!.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"FTS5 rebuild failed ({ex.Message}); run 'brainz reindex' to rebuild. " +
                "Exit code not affected.");
        }
    }

    // ─────────── Integrity check ───────────

    private async Task<bool> IntegrityCheckAsync(
        JsonElement manifestRoot, ImportMode mode, List<string> warnings, CancellationToken ct)
    {
        var rowCountMismatch = false;

        if (mode == ImportMode.Replace && manifestRoot.TryGetProperty("counts", out var counts))
        {
            // Map every manifest type key to its authoritative SQL count.
            foreach (var kv in counts.EnumerateObject())
            {
                var expected = kv.Value.ValueKind == JsonValueKind.Number ? kv.Value.GetInt32() : 0;
                var actual = await ActualCountForTypeAsync(kv.Name, ct);
                if (actual != -1 && expected != actual)
                {
                    warnings.Add(
                        $"row-count mismatch on {kv.Name}: manifest said {expected}, DB has {actual}");
                    rowCountMismatch = true;
                }
            }
        }

        // Orphan checks (warnings only — never bump exit code).
        if (await CountAsync("""
            SELECT (SELECT COUNT(*) FROM decision_embeddings WHERE decision_id NOT IN (SELECT id FROM decisions))
                 + (SELECT COUNT(*) FROM principle_embeddings WHERE principle_id NOT IN (SELECT id FROM principles))
                 + (SELECT COUNT(*) FROM note_embeddings WHERE note_id NOT IN (SELECT id FROM notes))
            """, ct) is var orphanEmb and > 0)
        {
            warnings.Add($"{orphanEmb} embedding row(s) reference missing entities; run 'brainz reindex' to clean up");
        }

        return rowCountMismatch;
    }

    private async Task<int> ActualCountForTypeAsync(string type, CancellationToken ct) => type switch
    {
        "project"        => await CountAsync("SELECT COUNT(*) FROM projects", ct),
        "project_remote" => await CountAsync("SELECT COUNT(*) FROM project_remotes", ct),
        "decision"       => await CountAsync("SELECT COUNT(*) FROM decisions", ct),
        "alternative"    => await CountAsync("SELECT COUNT(*) FROM alternatives", ct),
        "principle"      => await CountAsync("SELECT COUNT(*) FROM principles", ct),
        "note"           => await CountAsync("SELECT COUNT(*) FROM notes", ct),
        "tag"            => await CountAsync("SELECT COUNT(*) FROM tags", ct),
        "tag_link"       => await CountAsync("SELECT (SELECT COUNT(*) FROM decision_tags) + (SELECT COUNT(*) FROM principle_tags) + (SELECT COUNT(*) FROM note_tags)", ct),
        "link"           => await CountAsync("SELECT COUNT(*) FROM links", ct),
        "embedding"      => await CountAsync("SELECT (SELECT COUNT(*) FROM decision_embeddings) + (SELECT COUNT(*) FROM principle_embeddings) + (SELECT COUNT(*) FROM note_embeddings)", ct),
        "history_entry"  => await CountAsync("SELECT (SELECT COUNT(*) FROM decisions_history) + (SELECT COUNT(*) FROM principles_history) + (SELECT COUNT(*) FROM notes_history)", ct),
        _ => -1, // unknown type, skip comparison
    };

    private async Task<int> CountAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private static bool IsDiskFull(Exception ex) =>
        ex is IOException io &&
        (io.HResult == unchecked((int)0x80070070)
         || io.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase));
}
