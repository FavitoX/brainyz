// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.IO.Compression;
using Brainyz.Core;
using Brainyz.Core.Backup;
using Brainyz.Core.Errors;
using Brainyz.Core.Storage;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz backup --to &lt;path.zip&gt; [--compression fastest|optimal|none]
/// [--db &lt;path&gt;]</c> — atomic snapshot of the brainyz DB packaged as a
/// <c>.zip</c> containing the VACUUM'd DB plus a manifest + README.
/// </summary>
public static class BackupCommand
{
    public static Command Build()
    {
        var cmd = new Command("backup", "Create a zipped backup of the brainyz DB");

        var toOpt = new Option<string?>("--to")
        { Description = "Destination .zip path (required)" };
        var compOpt = new Option<string>("--compression")
        {
            Description = "Compression level: fastest (default), optimal, none",
            DefaultValueFactory = _ => "fastest"
        };
        var dbOpt = new Option<string?>("--db")
        { Description = "Override the default brainyz DB path" };

        cmd.Options.Add(toOpt);
        cmd.Options.Add(compOpt);
        cmd.Options.Add(dbOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var to = pr.GetValue(toOpt);
            if (string.IsNullOrWhiteSpace(to))
                throw new BrainyzException(
                    ErrorCode.BZ_BACKUP_ZIP_WRITE_FAILED,
                    "--to is required",
                    tip: "pass the output zip path, e.g. `brainz backup --to brain.zip`");

            var compStr = (pr.GetValue(compOpt) ?? "fastest").ToLowerInvariant();
            var level = compStr switch
            {
                "fastest" => CompressionLevel.Fastest,
                "optimal" => CompressionLevel.Optimal,
                "none"    => CompressionLevel.NoCompression,
                _ => throw new BrainyzException(
                    ErrorCode.BZ_BACKUP_ZIP_WRITE_FAILED,
                    $"unknown --compression '{compStr}'",
                    tip: "use fastest | optimal | none"),
            };

            var dbArg = pr.GetValue(dbOpt);
            var dbPath = dbArg ?? BrainyzPaths.Default().DbPath;

            if (!File.Exists(dbPath))
                throw new BrainyzException(
                    ErrorCode.BZ_BACKUP_DB_NOT_FOUND,
                    $"no brainyz DB at '{dbPath}'",
                    tip: "run `brainz init` or pass --db to point at an existing DB");

            await using var store = await BrainStore.OpenAsync(dbPath, ct: ct);
            var backup = new ZipBackup(store, BrainyzCliVersion.Current);
            await backup.BackupAsync(to, level, ct);

            var size = new FileInfo(to).Length;
            Console.WriteLine($"backup written to {to} ({FormatSize(size)})");
        });

        return cmd;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };
}
