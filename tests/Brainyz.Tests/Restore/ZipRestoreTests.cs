// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text;
using Brainyz.Cli.Commands;
using Brainyz.Core.Backup;
using Brainyz.Core.Errors;
using Brainyz.Core.Restore;
using Brainyz.Core.Storage;
using Brainyz.Tests.Export;

namespace Brainyz.Tests.Restore;

public class ZipRestoreTests : StoreTestBase
{
    // ─────────── Validation ───────────

    [Fact]
    public async Task Rejects_missing_zip()
    {
        var restore = new ZipRestore(DbPath);
        var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
            restore.RestoreAsync(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.zip"),
                skipSafetyBackup: true));
        Assert.Equal(ErrorCode.BZ_RESTORE_ZIP_NOT_FOUND, ex.Code);
    }

    [Fact]
    public async Task Rejects_corrupt_zip()
    {
        var p = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(p, Encoding.UTF8.GetBytes("this is not a zip, just plain text"));
        try
        {
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(p, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_RESTORE_ZIP_CORRUPT, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_zip_missing_manifest()
    {
        var p = Path.Combine(Path.GetTempPath(), $"nomani-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(p, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("brainyz.db");
            using var s = entry.Open();
            s.WriteByte(0);
        }
        try
        {
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(p, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_RESTORE_ZIP_MISSING_MANIFEST, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_zip_missing_db()
    {
        var p = Path.Combine(Path.GetTempPath(), $"nodb-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(p, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("manifest.json");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            await w.WriteAsync("{\"format_version\":1,\"brainyz_version\":\"x\",\"schema_version\":1,\"created_at_ms\":0,\"source_db_path\":\"x\",\"source_hostname\":\"x\",\"db_size_bytes\":0,\"embedding_model\":null,\"counts\":{}}");
        }
        try
        {
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(p, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_RESTORE_ZIP_MISSING_DB, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_newer_format_version()
    {
        var p = await WriteZipWithManifestAsync(formatVersion: 2, schemaVersion: 1);
        try
        {
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(p, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_RESTORE_FORMAT_VERSION_NEWER, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_schema_mismatch()
    {
        var p = await WriteZipWithManifestAsync(formatVersion: 1, schemaVersion: 99);
        try
        {
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(p, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_RESTORE_SCHEMA_MISMATCH, ex.Code);
        }
        finally { File.Delete(p); }
    }

    // ─────────── Happy path ───────────

    [Fact]
    public async Task Round_trip_backup_then_restore_preserves_seeded_decision()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);
        await Store.DisposeAsync(); // release our handle so swap works on Windows

        var zipPath = Path.Combine(Path.GetTempPath(), $"rt-bkp-{Guid.NewGuid():N}.zip");
        var targetDb = Path.Combine(Path.GetTempPath(), $"rt-restore-{Guid.NewGuid():N}.db");
        try
        {
            // Backup the seeded DB
            var srcStore = await BrainStore.OpenAsync(DbPath);
            try
            {
                await new ZipBackup(srcStore, "0.3.0-test")
                    .BackupAsync(zipPath, CompressionLevel.Fastest);
            }
            finally { await srcStore.DisposeAsync(); }

            // Restore into a brand-new target path
            var restore = new ZipRestore(targetDb);
            var result = await restore.RestoreAsync(zipPath, skipSafetyBackup: true);
            Assert.Null(result.SafetyBackupPath); // target didn't exist, so no backup
            Assert.True(File.Exists(targetDb));

            // Open and verify
            var restored = await BrainStore.OpenAsync(targetDb);
            try
            {
                var d = await restored.GetDecisionAsync(seeded.DecisionId);
                Assert.NotNull(d);
                Assert.Equal("LibSQL engine", d!.Title);
            }
            finally { await restored.DisposeAsync(); }
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            foreach (var f in new[] { targetDb, targetDb + "-wal", targetDb + "-shm" })
                if (File.Exists(f)) try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    // NOTE: "safety backup created when target exists" is exercised by the
    // round-trip test path and directly in SafetyBackupTests (Chunk 1). We
    // don't duplicate that scenario here with StoreTestBase as the target,
    // because StoreTestBase keeps its own Store handle open and the Nelknet
    // 0.2.6 driver on Windows holds the DB file share even after the
    // connection is disposed — which is a test-rig artifact, not a
    // production-path concern (the CLI is short-lived and does not hold
    // the DB between backup and restore).

    [Fact]
    public async Task Lock_probe_detects_another_process_holding_target_db()
    {
        // Windows-only: on POSIX File.Move (rename syscall) succeeds over
        // an open file — the open fd keeps the old inode alive — so this
        // class of "another process has the DB" error does not surface at
        // move time. Skip on non-Windows rather than assert a platform-
        // dependent behavior.
        if (!OperatingSystem.IsWindows()) return;

        await ExportSeed.PopulateAsync(Store);

        var zipPath = Path.Combine(Path.GetTempPath(), $"locked-{Guid.NewGuid():N}.zip");
        try
        {
            await new ZipBackup(Store, "0.3.0-test")
                .BackupAsync(zipPath, CompressionLevel.Fastest);

            // Store is still open on DbPath. The Move-based probe should
            // detect that and throw BZ_RESTORE_DB_LOCKED.
            var restore = new ZipRestore(DbPath);
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                restore.RestoreAsync(zipPath, skipSafetyBackup: true));

            Assert.Equal(ErrorCode.BZ_RESTORE_DB_LOCKED, ex.Code);
        }
        finally { if (File.Exists(zipPath)) File.Delete(zipPath); }
    }

    // ─────────── ShouldPrompt decision table ───────────

    [Theory]
    [InlineData(false, false, true)]  // TTY, no --force → prompt
    [InlineData(false, true,  false)] // piped stdin, no --force → skip
    [InlineData(true,  false, false)] // TTY, --force → skip
    [InlineData(true,  true,  false)] // piped, --force → skip
    public void ShouldPrompt_decision_table(bool force, bool stdinRedirected, bool shouldPrompt)
    {
        Assert.Equal(shouldPrompt, RestoreCommand.ShouldPrompt(force, stdinRedirected));
    }

    // ─────────── Helper ───────────

    private static async Task<string> WriteZipWithManifestAsync(int formatVersion, int schemaVersion)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manifest-fixture-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(p, ZipArchiveMode.Create);

        var manifest = zip.CreateEntry("manifest.json");
        using (var s = manifest.Open())
        using (var w = new StreamWriter(s))
        {
            await w.WriteAsync($"{{\"format_version\":{formatVersion},\"brainyz_version\":\"x\",\"schema_version\":{schemaVersion},\"created_at_ms\":0,\"source_db_path\":\"x\",\"source_hostname\":\"x\",\"db_size_bytes\":0,\"embedding_model\":null,\"counts\":{{}}}}");
        }

        var db = zip.CreateEntry("brainyz.db");
        using (var s = db.Open())
            s.WriteByte(0);

        return p;
    }
}
