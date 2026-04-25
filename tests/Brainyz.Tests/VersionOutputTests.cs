// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Brainyz.Cli;

namespace Brainyz.Tests;

/// <summary>
/// Contract test: <c>brainz --version</c> must print exactly
/// <c>brainyz &lt;semver&gt;</c> (plus a trailing newline) to stdout and exit 0.
/// Package managers' post-install lint/test hooks assert on this literal shape,
/// so drift breaks every channel's CI at once.
/// </summary>
public class VersionOutputTests
{
    [Fact]
    public async Task Version_flag_prints_canonical_format()
    {
        string exePath = ResolveBrainzBinary();

        var psi = new ProcessStartInfo(exePath, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to spawn brainz");

        string stdout = await proc.StandardOutput.ReadToEndAsync();
        string stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        Assert.Equal(0, proc.ExitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal($"brainyz {BrainyzCliVersion.Current}{Environment.NewLine}", stdout);
    }

    /// <summary>
    /// Locate the built CLI binary. Tests run from
    /// <c>&lt;repo&gt;/tests/Brainyz.Tests/bin/&lt;config&gt;/&lt;tfm&gt;/</c>; the CLI sits at
    /// <c>&lt;repo&gt;/src/Brainyz.Cli/bin/&lt;config&gt;/&lt;tfm&gt;/brainz[.exe]</c>.
    /// Walk up 5 levels (tfm → config → bin → Brainyz.Tests → tests) to reach
    /// the repo root, then descend into the CLI's matching bin tree.
    /// </summary>
    private static string ResolveBrainzBinary()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string tfm = Path.GetFileName(baseDir);                                  // e.g. net10.0
        string configDir = Path.GetFileName(Path.GetDirectoryName(baseDir)!);    // e.g. Release
        string repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

        string fileName = OperatingSystem.IsWindows() ? "brainz.exe" : "brainz";
        string cliBinary = Path.Combine(repoRoot, "src", "Brainyz.Cli", "bin", configDir, tfm, fileName);
        if (!File.Exists(cliBinary))
        {
            throw new FileNotFoundException(
                $"Built CLI binary not found at {cliBinary}. " +
                "Run `dotnet build src/Brainyz.Cli/Brainyz.Cli.csproj` before running this test.",
                cliBinary);
        }
        return cliBinary;
    }
}
