// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Storage;

/// <summary>
/// Access layer for the brain. Opens/creates the LibSQL database, applies the
/// embedded schema, and exposes typed CRUD per entity.
/// </summary>
/// <remarks>
/// <para>
/// Per-entity methods live in partial files (<c>BrainStore.Projects.cs</c>,
/// <c>BrainStore.Decisions.cs</c>, …). This file holds the shared plumbing:
/// connection lifetime, clock, disposal.
/// </para>
/// <para>
/// The store owns a single <see cref="LibSQLConnection"/>. One process should
/// keep a single instance. Multiple processes can target the same file
/// (LibSQL/SQLite handle the locking), but serializing concurrent writes is
/// the caller's concern.
/// </para>
/// </remarks>
public sealed partial class BrainStore : IAsyncDisposable
{
    private readonly LibSQLConnection _conn;
    private readonly IClock _clock;

    private BrainStore(LibSQLConnection conn, IClock clock)
    {
        _conn = conn;
        _clock = clock;
    }

    public static async Task<BrainStore> OpenAsync(
        string dbPath,
        IClock? clock = null,
        CancellationToken ct = default)
    {
        // NOTE: Nelknet 0.2.4 docs mention EnableStatementCaching in the
        // connection string, but the current build rejects that keyword.
        // Revisit when 0.3+ ships; for now the default behaviour is fine.
        var conn = new LibSQLConnection($"Data Source={dbPath}");
        await conn.OpenAsync(ct);
        await Schema.EnsureAppliedAsync(conn, ct);
        return new BrainStore(conn, clock ?? SystemClock.Instance);
    }

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
