// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Errors;
using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Safety;

/// <summary>
/// Pre-operation snapshot helper. Creates
/// <c>&lt;db-dir&gt;/brainyz.pre-&lt;operation&gt;-YYYYMMDD-HHMMSS.db</c>
/// next to the source DB via <c>VACUUM INTO</c>, so any destructive
/// operation (import replace, restore) has an automatic rollback target.
/// Invoked by ImportCommand and RestoreCommand. See §2 "Safety
/// invariants" of the v0.3 spec.
/// </summary>
public static class SafetyBackup
{
    public sealed record Result(string BackupPath);

    /// <summary>
    /// Writes a VACUUM INTO snapshot of <paramref name="sourceDbPath"/> to
    /// a sibling path tagged with the operation name and a timestamp.
    /// </summary>
    /// <param name="sourceDbPath">Path to the DB to snapshot.</param>
    /// <param name="operation">Short tag — "import" or "restore".</param>
    /// <param name="failureCode">Error code to use if snapshot fails.
    /// Defaults to <see cref="ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<Result> CreateAsync(
        string sourceDbPath,
        string operation,
        ErrorCode failureCode = ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(sourceDbPath)
            ?? throw new BrainyzException(failureCode,
                $"cannot derive directory from path '{sourceDbPath}'");

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var name = $"brainyz.pre-{operation}-{timestamp}.db";
        var backupPath = Path.Combine(dir, name);

        try
        {
            var cs = new LibSQLConnectionStringBuilder { DataSource = sourceDbPath }.ConnectionString;
            await using var conn = new LibSQLConnection(cs);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            // SQLite/LibSQL's VACUUM INTO requires a string literal target
            // (the statement does not accept bind parameters for this
            // argument). We inline the path and escape single quotes the
            // standard SQL way — doubling them.
            var escaped = backupPath.Replace("'", "''");
            cmd.CommandText = $"VACUUM INTO '{escaped}'";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not BrainyzException)
        {
            throw new BrainyzException(
                failureCode,
                $"failed to create safety backup at '{backupPath}': {ex.Message}",
                tip: "check disk space and that the target directory is writable",
                inner: ex);
        }

        return new Result(backupPath);
    }
}
