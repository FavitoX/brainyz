// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Errors;

namespace Brainyz.Core.Safety;

/// <summary>
/// Pre-operation snapshot helper. Creates
/// <c>&lt;db-dir&gt;/brainyz.pre-&lt;operation&gt;-YYYYMMDD-HHMMSS.db</c>
/// next to the source DB via <c>File.Copy</c>, so any destructive
/// operation (import replace, restore) has an automatic rollback target.
/// Invoked by <c>JsonlImporter</c> and <c>ZipRestore</c>. See §2
/// "Safety invariants" of the v0.3 spec.
/// </summary>
/// <remarks>
/// <para>
/// An earlier draft of this helper used <c>VACUUM INTO</c> through a
/// fresh <c>LibSQLConnection</c>, but on Linux that fails with
/// <c>libSQL error 2: Internal logic error in SQLite</c> whenever the
/// same DB file has another live connection somewhere (typical during
/// the importer's own workflow, since <c>JsonlImporter._store</c>
/// already holds an open handle). File-level copy sidesteps the driver
/// entirely and works the same on Windows, Linux, and macOS.
/// </para>
/// <para>
/// Correctness: we copy the main DB file plus any <c>-wal</c> and
/// <c>-shm</c> sidecars. For both callers (import replace, restore),
/// the destructive operation that follows overwrites the DB wholesale,
/// so any concurrent writer would be clobbered regardless of whether
/// the snapshot captured their in-flight state.
/// </para>
/// </remarks>
public static class SafetyBackup
{
    public sealed record Result(string BackupPath);

    /// <summary>
    /// Writes a file-level snapshot of <paramref name="sourceDbPath"/>
    /// (plus any -wal/-shm sidecars) to a sibling path tagged with the
    /// operation name and a timestamp.
    /// </summary>
    /// <param name="sourceDbPath">Path to the DB to snapshot.</param>
    /// <param name="operation">Short tag — "import" or "restore".</param>
    /// <param name="failureCode">Error code to use if snapshot fails.
    /// Defaults to <see cref="ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED"/>.</param>
    /// <param name="ct">Cancellation token (unused — File.Copy is
    /// synchronous on all platforms; kept for API symmetry).</param>
    public static Task<Result> CreateAsync(
        string sourceDbPath,
        string operation,
        ErrorCode failureCode = ErrorCode.BZ_IMPORT_SAFETY_BACKUP_FAILED,
        CancellationToken ct = default)
    {
        _ = ct;

        var dir = Path.GetDirectoryName(sourceDbPath)
            ?? throw new BrainyzException(failureCode,
                $"cannot derive directory from path '{sourceDbPath}'");

        // Millisecond resolution (fff) so back-to-back calls with the same
        // operation don't collide. Second-level granularity was fine when
        // the helper used VACUUM INTO (failures landed before a file was
        // written), but the current File.Copy implementation materialises
        // the file eagerly — two calls within the same wall-clock second
        // (common on fast Linux CI between serial xUnit tests) would fail
        // the second File.Copy(..., overwrite: false) with "file already
        // exists". The readable filename is kept; the extra three digits
        // are cheap.
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var name = $"brainyz.pre-{operation}-{timestamp}.db";
        var backupPath = Path.Combine(dir, name);

        try
        {
            File.Copy(sourceDbPath, backupPath, overwrite: false);
            foreach (var ext in new[] { "-wal", "-shm" })
            {
                var sidecar = sourceDbPath + ext;
                if (File.Exists(sidecar))
                    File.Copy(sidecar, backupPath + ext, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is not BrainyzException)
        {
            throw new BrainyzException(
                failureCode,
                $"failed to create safety backup at '{backupPath}': {ex.Message}",
                tip: "check disk space and that the target directory is writable",
                inner: ex);
        }

        return Task.FromResult(new Result(backupPath));
    }
}
