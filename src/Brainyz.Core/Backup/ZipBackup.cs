// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.Json;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Storage;
using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Backup;

/// <summary>
/// <c>brainz backup</c>'s engine. Produces a <c>.zip</c> containing a
/// <c>VACUUM INTO</c> snapshot of the DB plus a <c>manifest.json</c> and
/// a <c>README.txt</c>. Atomic, self-contained, restorable by
/// <see cref="Restore.ZipRestore"/> (Chunk 5). See §5 of the v0.3 spec.
/// </summary>
public sealed class ZipBackup
{
    private readonly BrainStore _store;
    private readonly string _brainyzVersion;

    public ZipBackup(BrainStore store, string brainyzVersion)
    {
        _store = store;
        _brainyzVersion = brainyzVersion;
    }

    public async Task BackupAsync(
        string outZipPath,
        CompressionLevel compression = CompressionLevel.Fastest,
        CancellationToken ct = default)
    {
        if (!File.Exists(_store.DbPath))
            throw new BrainyzException(
                ErrorCode.BZ_BACKUP_DB_NOT_FOUND,
                $"source DB '{_store.DbPath}' not found");

        var tempDir = Directory.CreateTempSubdirectory("brainyz-backup-").FullName;
        try
        {
            var vacDb = Path.Combine(tempDir, "brainyz.db");
            await VacuumIntoAsync(vacDb, ct);

            var manifest = await BuildManifestAsync(vacDb, ct);
            File.WriteAllText(
                Path.Combine(tempDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, typeof(BackupManifest), BrainyzJsonlContext.Default));

            File.WriteAllText(Path.Combine(tempDir, "README.txt"), ReadmeText(manifest));

            try
            {
                if (File.Exists(outZipPath)) File.Delete(outZipPath);
                ZipFile.CreateFromDirectory(tempDir, outZipPath, compression, includeBaseDirectory: false);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                throw new BrainyzException(
                    ErrorCode.BZ_BACKUP_DISK_FULL,
                    "disk full while writing the backup zip", inner: ex);
            }
            catch (Exception ex)
            {
                throw new BrainyzException(
                    ErrorCode.BZ_BACKUP_ZIP_WRITE_FAILED,
                    $"could not write backup zip '{outZipPath}': {ex.Message}",
                    tip: "check the target directory exists and is writable",
                    inner: ex);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task VacuumIntoAsync(string destPath, CancellationToken ct)
    {
        try
        {
            var cs = new LibSQLConnectionStringBuilder { DataSource = _store.DbPath }.ConnectionString;
            await using var conn = new LibSQLConnection(cs);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            // VACUUM INTO requires a string literal; escape single quotes.
            var escaped = destPath.Replace("'", "''");
            cmd.CommandText = $"VACUUM INTO '{escaped}'";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new BrainyzException(
                ErrorCode.BZ_BACKUP_DISK_FULL,
                "disk full during VACUUM INTO", inner: ex);
        }
        catch (BrainyzException) { throw; }
        catch (Exception ex)
        {
            throw new BrainyzException(
                ErrorCode.BZ_BACKUP_VACUUM_FAILED,
                $"VACUUM INTO failed: {ex.Message}",
                tip: "check the DB is not corrupted (PRAGMA integrity_check) and no other process holds a write lock",
                inner: ex);
        }
    }

    private async Task<BackupManifest> BuildManifestAsync(string vacDbPath, CancellationToken ct)
    {
        var info = new FileInfo(vacDbPath);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["decisions"]  = (await _store.ListAllDecisionsAsync(limit: int.MaxValue, ct: ct)).Count,
            ["principles"] = (await _store.ListAllPrinciplesAsync(activeOnly: false, ct: ct)).Count,
            ["notes"]      = (await _store.ListAllNotesAsync(activeOnly: false, limit: int.MaxValue, ct: ct)).Count,
        };

        return new BackupManifest(
            FormatVersion: 1,
            BrainyzVersion: _brainyzVersion,
            SchemaVersion: SchemaVersion.Current,
            CreatedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SourceDbPath: _store.DbPath,
            SourceHostname: Environment.MachineName,
            DbSizeBytes: info.Length,
            EmbeddingModel: null,
            Counts: counts);
    }

    private static bool IsDiskFull(Exception ex) =>
        ex is IOException io &&
        (io.HResult == unchecked((int)0x80070070)
         || io.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase));

    private static string ReadmeText(BackupManifest m) =>
        $"""
         brainyz backup
         ==============

         Created: {DateTimeOffset.FromUnixTimeMilliseconds(m.CreatedAtMs):yyyy-MM-dd HH:mm:ss} UTC
         Host:    {m.SourceHostname}
         Source:  {m.SourceDbPath}
         brainyz: v{m.BrainyzVersion}
         Schema:  v{m.SchemaVersion}

         Contents
         --------
         - manifest.json   metadata for `brainz restore`
         - brainyz.db      VACUUM INTO snapshot of the source DB
         - README.txt      this file

         Restore with:  brainz restore --from <this zip>
         """;
}
