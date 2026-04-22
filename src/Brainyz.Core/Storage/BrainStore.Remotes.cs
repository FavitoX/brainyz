// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddProjectRemoteAsync(ProjectRemote r, CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO project_remotes (id, project_id, remote_url, role, created_at)
            VALUES (@id, @pid, @url, @role, @created)
            """;
        cmd.Bind("@id", r.Id);
        cmd.Bind("@pid", r.ProjectId);
        cmd.Bind("@url", r.RemoteUrl);
        cmd.Bind("@role", EncodeRemoteRole(r.Role));
        cmd.Bind("@created", r.CreatedAtMs == 0 ? now : r.CreatedAtMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Project?> FindProjectByRemoteUrlAsync(
        string remoteUrl,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.slug, p.name, p.description, p.created_at, p.updated_at
            FROM projects p
            JOIN project_remotes r ON r.project_id = p.id
            WHERE r.remote_url = @url
            LIMIT 1
            """;
        cmd.Bind("@url", remoteUrl);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        return await rdr.ReadAsync(ct)
            ? new Project(
                Id: rdr.GetStringSafe(0),
                Slug: rdr.GetStringSafe(1),
                Name: rdr.GetStringSafe(2),
                Description: rdr.GetStringOrNull(3),
                CreatedAtMs: rdr.GetInt64Safe(4),
                UpdatedAtMs: rdr.GetInt64Safe(5))
            : null;
    }

    public async Task<IReadOnlyList<ProjectRemote>> ListRemotesForProjectAsync(
        string projectId,
        CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, remote_url, role, created_at
            FROM project_remotes
            WHERE project_id = @pid
            ORDER BY created_at
            """;
        cmd.Bind("@pid", projectId);
        var results = new List<ProjectRemote>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            results.Add(new ProjectRemote(
                Id: rdr.GetStringSafe(0),
                ProjectId: rdr.GetStringSafe(1),
                RemoteUrl: rdr.GetStringSafe(2),
                Role: DecodeRemoteRole(rdr.GetStringSafe(3)),
                CreatedAtMs: rdr.GetInt64Safe(4)));
        }
        return results;
    }

    private static string EncodeRemoteRole(RemoteRole role) => role switch
    {
        RemoteRole.Origin => "origin",
        RemoteRole.Upstream => "upstream",
        RemoteRole.Fork => "fork",
        RemoteRole.Mirror => "mirror",
        RemoteRole.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static RemoteRole DecodeRemoteRole(string s) => s switch
    {
        "origin" => RemoteRole.Origin,
        "upstream" => RemoteRole.Upstream,
        "fork" => RemoteRole.Fork,
        "mirror" => RemoteRole.Mirror,
        "other" => RemoteRole.Other,
        _ => throw new InvalidDataException($"Unknown remote role: '{s}'")
    };
}
