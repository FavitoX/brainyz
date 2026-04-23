// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Restore;

namespace Brainyz.Cli.Commands;

/// <summary>
/// <c>brainz restore --from &lt;path.zip&gt; [--no-backup] [--force]
/// [--db &lt;path&gt;]</c> — replace the local brainyz DB with the contents
/// of a backup zip. A pre-restore safety snapshot of the current DB is
/// written alongside it unless <c>--no-backup</c> is passed. Interactive
/// TTY stdin prompts for confirmation; piped stdin / <c>--force</c>
/// skip the prompt and proceed (see spec §5.5).
/// </summary>
public static class RestoreCommand
{
    public static Command Build()
    {
        var cmd = new Command("restore", "Restore a brainyz backup zip");

        var fromOpt = new Option<string?>("--from")
        { Description = "Path to the backup zip (required)" };
        var noBackupOpt = new Option<bool>("--no-backup")
        { Description = "Skip the automatic pre-restore safety backup" };
        var forceOpt = new Option<bool>("--force")
        { Description = "Skip the interactive confirmation prompt" };
        var dbOpt = new Option<string?>("--db")
        { Description = "Override the default brainyz DB path" };

        cmd.Options.Add(fromOpt);
        cmd.Options.Add(noBackupOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(dbOpt);

        cmd.SetAction(async (pr, ct) =>
        {
            var from = pr.GetValue(fromOpt);
            if (string.IsNullOrWhiteSpace(from))
                throw new BrainyzException(
                    ErrorCode.BZ_RESTORE_ZIP_NOT_FOUND,
                    "--from is required",
                    tip: "pass the backup zip path, e.g. `brainz restore --from brain.zip`");

            var skipBackup = pr.GetValue(noBackupOpt);
            var force = pr.GetValue(forceOpt);
            var dbArg = pr.GetValue(dbOpt);
            var dbPath = dbArg ?? BrainyzPaths.Default().DbPath;

            // Prompt gate: prompt iff stdin is an interactive TTY AND --force
            // is not passed. Piped/redirected stdin behaves like --force.
            if (ShouldPrompt(force, Console.IsInputRedirected))
            {
                Console.WriteLine($"Restoring backup from {from}");
                Console.WriteLine($"  target DB: {dbPath}");
                Console.Write("Proceed? [y/N] ");
                var ans = Console.ReadLine();
                if (ans is null || !ans.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
                    throw new BrainyzException(
                        ErrorCode.BZ_RESTORE_USER_DECLINED,
                        "restore declined by user");
            }

            var restore = new ZipRestore(dbPath);
            var result = await restore.RestoreAsync(from, skipSafetyBackup: skipBackup, ct);

            Console.WriteLine($"restored from {from}");
            Console.WriteLine($"  original: {result.SourceHostname} @ {DateTimeOffset.FromUnixTimeMilliseconds(result.SourceCreatedAtMs):yyyy-MM-dd HH:mm} UTC");
            if (result.SafetyBackupPath is not null)
                Console.WriteLine($"  pre-restore state saved to: {result.SafetyBackupPath}");
        });

        return cmd;
    }

    /// <summary>
    /// Prompt iff the user is at an interactive TTY AND didn't pass
    /// <c>--force</c>. Exposed internal for unit coverage.
    /// </summary>
    internal static bool ShouldPrompt(bool forceFlag, bool stdinIsRedirected) =>
        !forceFlag && !stdinIsRedirected;
}
