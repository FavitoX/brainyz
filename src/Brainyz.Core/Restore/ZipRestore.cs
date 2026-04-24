// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.Json;
using Brainyz.Core.Backup;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Safety;

namespace Brainyz.Core.Restore;

/// <summary>
/// <c>brainz restore</c>'s engine. Unzips a backup produced by
/// <see cref="ZipBackup"/>, validates its manifest, writes a pre-restore
/// safety snapshot of the current DB, probes for external locks on the
/// target file, then atomically swaps the extracted DB into place.
/// See §5.4 of the v0.3 spec.
/// </summary>
public sealed class ZipRestore
{
    private readonly string _targetDbPath;

    public ZipRestore(string targetDbPath)
    {
        _targetDbPath = targetDbPath;
    }

    public async Task<RestoreResult> RestoreAsync(
        string zipPath,
        bool skipSafetyBackup = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new BrainyzException(
                ErrorCode.BZ_RESTORE_ZIP_NOT_FOUND,
                $"backup zip '{zipPath}' not found");

        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_RESTORE_ZIP_CORRUPT,
                $"'{zipPath}' is not a valid ZIP archive: {ex.Message}",
                inner: ex);
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_ZIP_MISSING_MANIFEST,
                    "zip does not contain manifest.json",
                    tip: "zip was not produced by `brainz backup`; get the original archive");

            var dbEntry = archive.GetEntry("brainyz.db")
                ?? throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_ZIP_MISSING_DB,
                    "zip does not contain brainyz.db",
                    tip: "zip was not produced by `brainz backup`; get the original archive");

            BackupManifest manifest;
            string manifestJson;
            using (var s = manifestEntry.Open())
            using (var reader = new StreamReader(s))
                manifestJson = await reader.ReadToEndAsync(ct);

            try
            {
                manifest = JsonSerializer.Deserialize(manifestJson,
                    typeof(BackupManifest),
                    BrainyzJsonlContext.Default) as BackupManifest
                    ?? throw new BrainyzException(
                        ErrorCode.BZ_RESTORE_ZIP_CORRUPT,
                        "manifest.json is empty or not a valid backup manifest");
            }
            catch (JsonException ex)
            {
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_ZIP_CORRUPT,
                    $"manifest.json is not valid JSON: {ex.Message}",
                    inner: ex);
            }

            if (manifest.FormatVersion > 1)
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_FORMAT_VERSION_NEWER,
                    $"backup created for format version {manifest.FormatVersion}; this brainyz supports up to 1",
                    tip: "upgrade brainyz to restore newer backups");

            if (manifest.SchemaVersion != SchemaVersion.Current)
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_SCHEMA_MISMATCH,
                    $"backup schema v{manifest.SchemaVersion}, local schema v{SchemaVersion.Current}",
                    tip: "migration is not yet supported; restore on a matching brainyz version");

            // Safety backup of the current DB (if any) before touching the
            // target. We use File.Copy — NOT SafetyBackup.CreateAsync
            // (VACUUM INTO) — because opening a LibSQL connection on
            // Windows leaves the DB's file handle briefly in a state that
            // blocks the subsequent File.Move. File-level copy captures a
            // consistent snapshot for our purposes: we're about to replace
            // the DB wholesale anyway, so any concurrent writer has to
            // tolerate the swap. VACUUM INTO remains the right tool for
            // the import path, where we open the DB ourselves.
            //
            // Lock detection on the final swap: SQLite/LibSQL on Windows
            // opens DB files with FileShare.Read (no Delete share), which
            // makes File.Move fail when any other process still holds the
            // DB. That Move failure is classified as BZ_RESTORE_DB_LOCKED
            // below (see MoveAtomicallyAsync).
            string? safetyBackupPath = null;
            if (!skipSafetyBackup && File.Exists(_targetDbPath))
                safetyBackupPath = CopyPreRestoreSnapshot(_targetDbPath);

            var targetDir = Path.GetDirectoryName(_targetDbPath)
                ?? throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_SWAP_FAILED,
                    $"cannot resolve target directory from '{_targetDbPath}'");
            Directory.CreateDirectory(targetDir);

            var tempDb = Path.Combine(targetDir, $".brainyz.restore-{Guid.NewGuid():N}.tmp");
            try
            {
                try
                {
                    dbEntry.ExtractToFile(tempDb, overwrite: true);
                }
                catch (IOException ex) when (IsDiskFull(ex))
                {
                    throw new BrainyzException(
                        ErrorCode.BZ_RESTORE_DISK_FULL,
                        "disk full while extracting the DB from the zip",
                        inner: ex);
                }
                catch (Exception ex)
                {
                    throw new BrainyzException(
                        ErrorCode.BZ_RESTORE_SWAP_FAILED,
                        $"could not extract DB to '{tempDb}': {ex.Message}",
                        inner: ex);
                }

                await MoveAtomicallyAsync(tempDb, _targetDbPath, ct);
            }
            finally
            {
                if (File.Exists(tempDb))
                    try { File.Delete(tempDb); } catch { /* ignore */ }
            }

            return new RestoreResult(
                SourceHostname: manifest.SourceHostname,
                SourceCreatedAtMs: manifest.CreatedAtMs,
                SafetyBackupPath: safetyBackupPath);
        }
    }

    // Sharing-violation detection. On Windows File.Move throws IOException
    // with a Win32 HResult; the most common codes are ERROR_SHARING_VIOLATION
    // (32, 0x80070020), ERROR_LOCK_VIOLATION (33, 0x80070021) and
    // ERROR_ACCESS_DENIED (5, 0x80070005). The .NET runtime sometimes
    // exposes HResult=0x80070000|winerror, sometimes just -2147024xxx. Treat
    // any IOException whose low-16-bit Win32 code is one of the known
    // share/lock/access codes as a sharing violation. This keeps us
    // locale-agnostic (the message is translated on Spanish Windows).
    private static bool IsSharingViolation(IOException ex)
    {
        // .NET 10 on Windows: ex.HResult is typically 0x80070020 for sharing
        // violations on File.Move, but we've also observed bare Win32 codes
        // (32, 33, 5) on some paths. Check both forms.
        int h = ex.HResult;
        int winErr = h & 0xFFFF;
        return h is unchecked((int)0x80070020) or unchecked((int)0x80070021) or unchecked((int)0x80070005)
            || winErr is 32 or 33 or 5;
    }

    private static async Task MoveAtomicallyAsync(string src, string dest, CancellationToken ct)
    {
        // Retry Move to tolerate LibSQL's brief Windows-handle residue after
        // a just-closed connection (our SafetyBackup or the caller's).
        // File.Move on Windows raises both IOException and
        // UnauthorizedAccessException for shared-access conflicts — treat
        // both as "DB in use"; we never get either on a genuine permission/
        // filesystem error after a successful extract+safety-backup phase
        // that already wrote to the same directory.
        const int maxAttempts = 6;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(src, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsLockLike(ex))
            {
                if (attempt == 1)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                await Task.Delay(50 * attempt, ct);
            }
            catch (Exception ex) when (IsLockLike(ex))
            {
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_DB_LOCKED,
                    $"target DB '{dest}' is held by another process",
                    tip: "close any running `brainz mcp` or DB-tool windows and retry",
                    inner: ex);
            }
            catch (Exception ex)
            {
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_SWAP_FAILED,
                    $"could not swap restored DB into place: {ex.Message}",
                    tip: "ensure the target directory is writable and on the same filesystem as the extract temp",
                    inner: ex);
            }
        }
    }

    private static bool IsLockLike(Exception ex) =>
        ex is UnauthorizedAccessException
        || (ex is IOException io && IsSharingViolation(io));

    private static string CopyPreRestoreSnapshot(string sourceDbPath)
    {
        var dir = Path.GetDirectoryName(sourceDbPath)
            ?? throw new BrainyzException(
                ErrorCode.BZ_RESTORE_SAFETY_BACKUP_FAILED,
                $"cannot derive directory from '{sourceDbPath}'");
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(dir, $"brainyz.pre-restore-{timestamp}.db");

        try
        {
            File.Copy(sourceDbPath, backupPath, overwrite: false);

            // Also copy WAL and SHM sidecars if present — they contain
            // uncommitted writes the main file doesn't yet see.
            foreach (var ext in new[] { "-wal", "-shm" })
            {
                var sidecar = sourceDbPath + ext;
                if (File.Exists(sidecar))
                    File.Copy(sidecar, backupPath + ext, overwrite: false);
            }
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new BrainyzException(
                ErrorCode.BZ_RESTORE_DISK_FULL,
                $"disk full while writing pre-restore safety backup at '{backupPath}'",
                inner: ex);
        }
        catch (Exception ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_RESTORE_SAFETY_BACKUP_FAILED,
                $"failed to write pre-restore safety backup at '{backupPath}': {ex.Message}",
                tip: "check disk space and that the target directory is writable",
                inner: ex);
        }

        return backupPath;
    }

    private static bool IsDiskFull(Exception ex) =>
        ex is IOException io &&
        (io.HResult == unchecked((int)0x80070070)
         || io.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase));
}

public sealed record RestoreResult(
    string SourceHostname,
    long SourceCreatedAtMs,
    string? SafetyBackupPath);
