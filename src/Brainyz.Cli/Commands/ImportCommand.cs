// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Import;
using Brainyz.Core.Storage;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz import --from &lt;path.jsonl&gt; [--mode replace|additive]
/// [--no-backup] [--dry-run] [--db &lt;path&gt;]</c> — ingest a JSONL export
/// into the local brain. Replace mode wipes and rebuilds the tables from
/// the file; additive mode inserts only rows whose ULID is not yet
/// present. An automatic <c>brainyz.pre-import-*.db</c> snapshot is
/// written alongside the DB unless <c>--no-backup</c> is passed.
/// </summary>
public static class ImportCommand
{
    public static Command Build()
    {
        var cmd = new Command("import", "Import a JSONL export into the brainyz DB");

        var fromOpt = new Option<string?>("--from")
        { Description = "Path to the .jsonl file to import (required)" };
        var modeOpt = new Option<string>("--mode")
        { Description = "Import mode: 'replace' (default) or 'additive'", DefaultValueFactory = _ => "replace" };
        var noBackupOpt = new Option<bool>("--no-backup")
        { Description = "Skip the automatic pre-import safety backup (intended for CI/tests)" };
        var dryRunOpt = new Option<bool>("--dry-run")
        { Description = "Parse and validate the file without mutating the DB" };
        var dbOpt = new Option<string?>("--db")
        { Description = "Override the default brainyz DB path" };

        cmd.Options.Add(fromOpt);
        cmd.Options.Add(modeOpt);
        cmd.Options.Add(noBackupOpt);
        cmd.Options.Add(dryRunOpt);
        cmd.Options.Add(dbOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var from = pr.GetValue(fromOpt);
            if (string.IsNullOrWhiteSpace(from))
                throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_FILE_NOT_FOUND,
                    "--from is required",
                    tip: "pass the JSONL path, e.g. `brainz import --from brainyz.jsonl`");

            var modeStr = pr.GetValue(modeOpt) ?? "replace";
            var mode = modeStr.ToLowerInvariant() switch
            {
                "replace"  => ImportMode.Replace,
                "additive" => ImportMode.Additive,
                _ => throw new BrainyzException(
                    ErrorCode.BZ_IMPORT_RECORD_MALFORMED,
                    $"unknown --mode '{modeStr}'",
                    tip: "use --mode replace (default) or --mode additive"),
            };

            var skipSafety = pr.GetValue(noBackupOpt);
            var dryRun = pr.GetValue(dryRunOpt);
            var dbArg = pr.GetValue(dbOpt);

            var dbPath = dbArg ?? BrainyzPaths.Default().DbPath;
            await using var store = await BrainStore.OpenAsync(dbPath, ct: ct);

            var importer = new JsonlImporter(store, BrainyzCliVersion.Current);
            var result = await importer.ImportAsync(from, mode, skipSafety, dryRun, ct);

            // Report
            if (dryRun)
            {
                int total = 0;
                foreach (var v in result.InsertedCounts.Values) total += v;
                Console.WriteLine($"dry run OK — {total} records would be processed (no changes written)");
                return 0;
            }

            int inserted = 0;
            foreach (var v in result.InsertedCounts.Values) inserted += v;
            int skipped = 0;
            foreach (var v in result.SkippedCounts.Values) skipped += v;

            if (mode == ImportMode.Replace)
                Console.WriteLine($"imported {inserted} records from {from} (replace mode)");
            else
                Console.WriteLine($"added {inserted} records, skipped {skipped} existing (additive mode)");

            if (result.SafetyBackupPath is not null)
                Console.WriteLine($"pre-import backup saved to: {result.SafetyBackupPath}");

            foreach (var w in result.Warnings)
                Console.Error.WriteLine($"warning: {w}");

            // Per §4.4: replace-mode row-count mismatch bumps the exit code to 1.
            return result.RowCountMismatch ? 1 : 0;
        });

        return cmd;
    }
}
