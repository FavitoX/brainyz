// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddProjectAsync(Project p, CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects (id, slug, name, description, created_at, updated_at)
            VALUES (@id, @slug, @name, @desc, @created, @updated)
            """;
        cmd.Bind("@id", p.Id);
        cmd.Bind("@slug", p.Slug);
        cmd.Bind("@name", p.Name);
        cmd.Bind("@desc", p.Description);
        cmd.Bind("@created", p.CreatedAtMs == 0 ? now : p.CreatedAtMs);
        cmd.Bind("@updated", p.UpdatedAtMs == 0 ? now : p.UpdatedAtMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Project?> GetProjectAsync(string id, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, name, description, created_at, updated_at
            FROM projects WHERE id = @id
            """;
        cmd.Bind("@id", id);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? ReadProject(rdr) : null;
    }

    public async Task<Project?> GetProjectBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, name, description, created_at, updated_at
            FROM projects WHERE slug = @slug
            """;
        cmd.Bind("@slug", slug);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct) ? ReadProject(rdr) : null;
    }

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, name, description, created_at, updated_at
            FROM projects ORDER BY slug
            """;
        var results = new List<Project>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadProject(rdr));
        return results;
    }

    private static Project ReadProject(System.Data.Common.DbDataReader rdr) => new(
        Id: rdr.GetStringSafe(0),
        Slug: rdr.GetStringSafe(1),
        Name: rdr.GetStringSafe(2),
        Description: rdr.GetStringOrNull(3),
        CreatedAtMs: rdr.GetInt64Safe(4),
        UpdatedAtMs: rdr.GetInt64Safe(5));
}
