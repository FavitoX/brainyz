// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Brainyz.Cli;
using Brainyz.Cli.Commands;

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
root.Subcommands.Add(ReindexCommand.Build());
root.Subcommands.Add(McpCommand.Build());

return await root.Parse(args).InvokeAsync();
