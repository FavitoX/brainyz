// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Storage;

namespace Brainyz.Tests;

/// <summary>
/// Lifecycle tests: exercise <see cref="BrainStore.OpenAsync"/> itself.
/// Do not use <see cref="StoreTestBase"/> because the store is the
/// subject under test, not a fixture.
/// </summary>
public sealed class BrainStoreTests : IAsyncLifetime
{
    private string _dbPath = null!;

    public Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"brainyz-test-{Guid.NewGuid():N}.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        foreach (var f in new[] { _dbPath, _dbPath + "-journal", _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(f))
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task OpenAsync_creates_new_db_and_applies_schema()
    {
        Assert.False(File.Exists(_dbPath));

        await using var store = await BrainStore.OpenAsync(_dbPath);

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task Reopening_existing_db_is_idempotent()
    {
        await using (var s1 = await BrainStore.OpenAsync(_dbPath)) { /* schema applied */ }
        await using (var s2 = await BrainStore.OpenAsync(_dbPath)) { /* should not throw */ }
    }
}
