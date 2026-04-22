// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    /// <summary>
    /// Locates an entity by its id, probing decisions → principles → notes in
    /// that order. Returns the entity type when found, null otherwise. Useful
    /// for commands that accept a bare id (e.g. <c>brainz show</c>,
    /// <c>brainz link</c>) without forcing the caller to declare the type.
    /// </summary>
    public async Task<LinkEntity?> FindEntityByIdAsync(string id, CancellationToken ct = default)
    {
        if (await ExistsAsync("decisions", id, ct)) return LinkEntity.Decision;
        if (await ExistsAsync("principles", id, ct)) return LinkEntity.Principle;
        if (await ExistsAsync("notes", id, ct)) return LinkEntity.Note;
        return null;
    }

    private async Task<bool> ExistsAsync(string table, string id, CancellationToken ct)
    {
        await using var cmd = _conn.CreateCommand();
        // Table name is a compile-time constant — no injection risk. The
        // reader path is deliberate: Nelknet 0.2.5's ExecuteScalarAsync
        // occasionally returns null for valid rows in the add-then-read
        // pattern exercised by Update* tests, whereas the reader form is
        // reliable across all call sites.
        cmd.CommandText = $"SELECT 1 FROM {table} WHERE id = @id LIMIT 1";
        cmd.Bind("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct);
    }
}
