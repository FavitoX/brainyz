// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli;
using Brainyz.Cli.Commands;
using Brainyz.Core.Errors;

// Must run before anything touches Nelknet / LibSQL.
NativeLibraryResolver.Register();

var root = new RootCommand("brainyz — personal global knowledge layer for devs working with AI agents");

root.Subcommands.Add(InitCommand.Build());
root.Subcommands.Add(ScopeCommand.Build());
root.Subcommands.Add(AddCommand.Build());
root.Subcommands.Add(ListCommand.Build());
root.Subcommands.Add(SearchCommand.Build());
root.Subcommands.Add(ShowCommand.Build());
root.Subcommands.Add(LinkCommand.Build());
root.Subcommands.Add(EditCommand.Build());
root.Subcommands.Add(DeleteCommand.Build());
root.Subcommands.Add(ReindexCommand.Build());
root.Subcommands.Add(ExportCommand.Build());
root.Subcommands.Add(ImportCommand.Build());
root.Subcommands.Add(BackupCommand.Build());
root.Subcommands.Add(RestoreCommand.Build());
root.Subcommands.Add(McpCommand.Build());

// Replace the auto-attached VersionOption's action so `brainz --version`
// prints `brainyz <semver>` instead of the assembly version. Package-manager
// test hooks (Homebrew test_do, AUR check()) assert on this format.
//
// Single() is intentional: if a future System.CommandLine release stops
// auto-attaching VersionOption, we want a loud crash at startup rather than
// silently regressing the contract. VersionOutputTests pins the happy path.
var versionOption = root.Options.OfType<VersionOption>().Single();
versionOption.Action = new PrintVersionAction();

try
{
    return await root.Parse(args).InvokeAsync();
}
catch (BrainyzException ex)
{
    Console.Error.WriteLine(ErrorFormatter.Format(ex));
    return ErrorFormatter.ExitCodeFor(ex.Code);
}
