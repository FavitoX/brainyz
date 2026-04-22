// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Storage;

namespace Brainyz.Tests;

/// <summary>
/// Shared setup/teardown for tests that exercise <see cref="BrainStore"/>
/// end-to-end. Each test class gets its own temporary database file and a
/// ready-to-use store instance. Derived classes may override with <c>override</c>
/// (both methods are <c>virtual</c>) to layer their own setup/teardown.
/// </summary>
public abstract class StoreTestBase : IAsyncLifetime
{
    protected string DbPath { get; private set; } = null!;
    protected BrainStore Store { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"brainyz-test-{Guid.NewGuid():N}.db");
        Store = await BrainStore.OpenAsync(DbPath);
    }

    public virtual async Task DisposeAsync()
    {
        await Store.DisposeAsync();
        foreach (var f in new[] { DbPath, DbPath + "-journal", DbPath + "-wal", DbPath + "-shm" })
        {
            if (File.Exists(f))
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }
    }
}
