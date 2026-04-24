// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Storage;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz export --to &lt;path.jsonl&gt; [--project &lt;id&gt;]
/// [--include-history] [--db &lt;path&gt;]</c> — streams the brain (or a
/// filtered subset) to a JSONL file for backup, inspection, or additive
/// re-import elsewhere. See the v0.3 design spec §3 for the file format.
/// </summary>
public static class ExportCommand
{
    public static Command Build()
    {
        var cmd = new Command("export", "Export the brainyz brain as JSONL");

        var toOpt = new Option<string?>("--to")
        { Description = "Destination .jsonl path (required)" };
        var projectOpt = new Option<string?>("--project")
        { Description = "Optional project id to filter by; omit for the whole brain" };
        var includeHistoryOpt = new Option<bool>("--include-history")
        { Description = "Include history entries (off by default)" };
        var dbOpt = new Option<string?>("--db")
        { Description = "Override the default brainyz DB path" };

        cmd.Options.Add(toOpt);
        cmd.Options.Add(projectOpt);
        cmd.Options.Add(includeHistoryOpt);
        cmd.Options.Add(dbOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var to = pr.GetValue(toOpt);
            if (string.IsNullOrWhiteSpace(to))
                throw new BrainyzException(
                    ErrorCode.BZ_EXPORT_WRITE_FAILED,
                    "--to is required",
                    tip: "pass a destination path, e.g. `brainz export --to brainyz.jsonl`");

            var proj = pr.GetValue(projectOpt);
            var includeHistory = pr.GetValue(includeHistoryOpt);
            var dbArg = pr.GetValue(dbOpt);

            // BrainContext.OpenAsync uses the default path. Honor --db by
            // opening BrainStore directly at the override path — the
            // exporter only needs the store, not ScopeResolver or the
            // embedding services.
            if (dbArg is not null && !File.Exists(dbArg))
                throw new BrainyzException(
                    ErrorCode.BZ_EXPORT_DB_NOT_FOUND,
                    $"DB not found at '{dbArg}'");

            var dbPath = dbArg ?? BrainyzPaths.Default().DbPath;
            if (!File.Exists(dbPath))
                throw new BrainyzException(
                    ErrorCode.BZ_EXPORT_DB_NOT_FOUND,
                    $"no brainyz DB at '{dbPath}' — run `brainz init` or pass --db");

            await using var store = await BrainStore.OpenAsync(dbPath, ct: ct);

            var exporter = new JsonlExporter(store, brainyzVersion: BrainyzCliVersion.Current);
            var result = await exporter.ExportAsync(to, proj, includeHistory, ct);

            int total = 0;
            foreach (var v in result.Counts.Values) total += v;
            Console.WriteLine($"exported {total} records to {to}");
        });

        return cmd;
    }
}
