// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Storage;

/// <summary>
/// Bootstrap of the embedded schema. Idempotent — if the database is already at
/// the expected version this call is a no-op.
/// </summary>
public static class Schema
{
    public const string ExpectedVersion = "1";
    private const string EmbeddedResource = "brainyz.schema.sql";

    public static async Task EnsureAppliedAsync(LibSQLConnection conn, CancellationToken ct = default)
    {
        var version = await ReadSchemaVersionAsync(conn, ct);

        if (version is null)
        {
            await ApplyEmbeddedAsync(conn, ct);
            return;
        }

        if (version != ExpectedVersion)
            throw new InvalidOperationException(
                $"brainyz schema version mismatch: DB is '{version}', Core expects '{ExpectedVersion}'. " +
                "Migrations not implemented yet.");
    }

    private static async Task<string?> ReadSchemaVersionAsync(LibSQLConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='_meta'";
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;
        await rdr.CloseAsync();

        await using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "SELECT value FROM _meta WHERE key='schema_version'";
        var result = await versionCmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    private static async Task ApplyEmbeddedAsync(LibSQLConnection conn, CancellationToken ct)
    {
        var sql = LoadEmbeddedSchema();
        foreach (var stmt in SqlSplitter.Split(sql))
        {
            ct.ThrowIfCancellationRequested();
            await conn.ExecuteDrainingAsync(stmt, ct);
        }
    }

    private static string LoadEmbeddedSchema()
    {
        var asm = typeof(Schema).Assembly;
        using var stream = asm.GetManifestResourceStream(EmbeddedResource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResource}' not found in {asm.FullName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
