// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Brainyz.Cli;

/// <summary>
/// Custom action for the built-in <c>--version</c> option. Overrides the
/// default <see cref="VersionOption"/> behavior (which prints the assembly
/// version) so the CLI emits <c>"brainyz &lt;semver&gt;"</c> sourced from
/// <see cref="BrainyzCliVersion.Current"/>. Required so every package
/// manager's post-install lint or test step can assert on a stable format.
/// </summary>
internal sealed class PrintVersionAction : SynchronousCommandLineAction
{
    // Per System.CommandLine 2.0.0-beta7+ release notes: when overriding
    // the built-in Help/Version actions, set ClearsParseErrors = true so
    // unrelated parse errors do NOT suppress this action's output. The
    // default VersionOption.Action sets this, and our override must too.
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        Console.WriteLine($"brainyz {BrainyzCliVersion.Current}");
        return 0;
    }
}
