// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Errors;
using Brainyz.Core.Safety;
using Brainyz.Core.Storage;

namespace Brainyz.Tests.Safety;

public class SafetyBackupTests : StoreTestBase
{
    [Fact]
    public async Task Creates_pre_op_snapshot_next_to_source_db()
    {
        var result = await SafetyBackup.CreateAsync(DbPath, operation: "import");

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        var dir = Path.GetDirectoryName(DbPath)!;
        Assert.Equal(dir, Path.GetDirectoryName(result.BackupPath));
        Assert.Contains(".pre-import-", Path.GetFileName(result.BackupPath));
        Assert.EndsWith(".db", result.BackupPath);
    }

    [Fact]
    public async Task Operation_name_is_embedded_in_filename()
    {
        var result = await SafetyBackup.CreateAsync(DbPath, operation: "restore");
        Assert.Contains(".pre-restore-", Path.GetFileName(result.BackupPath));
    }

    [Fact]
    public async Task Wraps_io_failure_as_BrainyzException_with_given_code()
    {
        var badPath = Path.Combine(Path.GetTempPath(), "does-not-exist", "brainyz.db");

        var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
            SafetyBackup.CreateAsync(badPath, operation: "import",
                failureCode: ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED));

        Assert.Equal(ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED, ex.Code);
    }

    [Fact]
    public async Task Backup_file_opens_as_valid_libsql_database()
    {
        var result = await SafetyBackup.CreateAsync(DbPath, operation: "import");

        await using var copy = await BrainStore.OpenAsync(result.BackupPath);
        var decisions = await copy.ListAllDecisionsAsync(limit: 1);
        Assert.NotNull(decisions);
    }

    [Fact]
    public async Task Consecutive_calls_with_same_operation_produce_unique_paths()
    {
        // Regression: an earlier second-resolution timestamp format caused
        // two CreateAsync calls within the same wall-clock second to land
        // on the same backup filename, and File.Copy(..., overwrite:false)
        // threw "file already exists". Millisecond resolution keeps the
        // paths unique even under CI-fast back-to-back calls.
        var a = await SafetyBackup.CreateAsync(DbPath, operation: "import");
        var b = await SafetyBackup.CreateAsync(DbPath, operation: "import");

        Assert.NotEqual(a.BackupPath, b.BackupPath);
        Assert.True(File.Exists(a.BackupPath));
        Assert.True(File.Exists(b.BackupPath));
    }
}
