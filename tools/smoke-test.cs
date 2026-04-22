// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

// Usage (from the repository root):
//   dotnet run tools/smoke-test.cs            # publishes brainz first, then runs
//   BRAINZ_BINARY=/path/to/brainz dotnet run tools/smoke-test.cs  # uses provided binary
//
// Exits 0 on success, 1 on the first failed assertion. CI can wire this as a
// release-gate step after publishing the AOT binaries.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

var repoRoot = FindRepoRoot();
var configDir = Path.Combine(Path.GetTempPath(), $"brainyz-smoke-{Guid.NewGuid():N}");
var binary = Environment.GetEnvironmentVariable("BRAINZ_BINARY");
if (string.IsNullOrEmpty(binary) || !File.Exists(binary))
{
    Log("publishing brainz single-file (no AOT) for the smoke run…");
    binary = await PublishAsync(repoRoot);
}
Log($"binary : {binary}");
Log($"config : {configDir}");
Log("");

int step = 0;
try
{
    // 1. init
    Step("init creates the DB and prints ready");
    var initOut = await RunOk("init");
    Assert(initOut.Contains("brainyz ready"), $"expected 'brainyz ready' in stdout, got:\n{initOut}");
    Assert(File.Exists(Path.Combine(configDir, "brainyz.db")), "brainyz.db not created");

    // 2. add decision returns a ULID
    Step("add decision returns a ULID");
    var decisionId = await RunOk(
        "add decision \"Use LibSQL\" --text \"Embedded LibSQL with native vectors\" --confidence high");
    Assert(Regex.IsMatch(decisionId, "^[0-9A-Z]{26}$"),
        $"expected a 26-char ULID, got: '{decisionId}'");

    // 3. show reflects the stored fields
    Step("show prints title, confidence, body");
    var shown = await RunOk($"show {decisionId}");
    Assert(shown.Contains("Use LibSQL"), "title not in show output");
    Assert(shown.Contains("confidence : high"), "confidence not in show output");
    Assert(shown.Contains("Embedded LibSQL with native vectors"), "body not in show output");

    // 4. edit patches fields (title changes, body stays)
    Step("edit decision patches title + confidence, leaves body untouched");
    await RunOk($"edit decision {decisionId} --title \"Use LibSQL (revised)\" --confidence medium");
    var patched = await RunOk($"show {decisionId}");
    Assert(patched.Contains("Use LibSQL (revised)"), "edited title not reflected");
    Assert(patched.Contains("confidence : medium"), "edited confidence not reflected");
    Assert(patched.Contains("Embedded LibSQL with native vectors"),
        "untouched body was lost by edit");

    // 5. FTS search finds the edited decision
    Step("search --text-only finds the decision by its new title");
    var searchHits = await RunOk("search revised --text-only");
    Assert(searchHits.Contains(decisionId), $"search did not return the id:\n{searchHits}");

    // 6. add principle via stdin
    Step("add principle accepts body via stdin");
    var principleId = await RunOk(
        arguments: "add principle \"Op simplicity\"",
        stdin: "One file, one process, zero services\n");
    Assert(Regex.IsMatch(principleId, "^[0-9A-Z]{26}$"),
        $"expected a principle ULID, got: '{principleId}'");

    // 7. link between the two
    Step("link creates a cross-entity relationship");
    var linkId = await RunOk($"link {principleId} derived_from {decisionId}");
    Assert(Regex.IsMatch(linkId, "^[0-9A-Z]{26}$"),
        $"expected a link ULID, got: '{linkId}'");

    // 8. delete --force removes the decision; subsequent show fails
    Step("delete --force removes the decision");
    var deleteOut = await RunOk($"delete {decisionId} --force");
    Assert(deleteOut.Contains($"deleted decision {decisionId}"),
        $"expected 'deleted decision …', got: {deleteOut}");
    var afterDelete = await Run($"show {decisionId}");
    Assert(afterDelete.Code != 0, "show after delete must exit non-zero");
    Assert(afterDelete.Err.Contains("no entity with id"),
        $"expected 'no entity with id' on stderr, got: {afterDelete.Err}");

    // 9. delete without --force on redirected stdin is declined
    Step("delete without --force is declined when stdin is redirected");
    var decline = await Run($"delete {principleId}", stdin: "");
    Assert(decline.Code == 0, $"declined delete should exit 0, got {decline.Code}");
    var combined = decline.Out + "\n" + decline.Err;
    Assert(combined.Contains("declined"),
        $"expected 'declined' message, got: {combined}");
    var stillThere = await Run($"show {principleId}");
    Assert(stillThere.Code == 0, "principle should still exist after declined delete");

    Console.WriteLine();
    Console.WriteLine("✓ smoke test passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"✗ step {step} failed: {ex.Message}");
    return 1;
}
finally
{
    try { Directory.Delete(configDir, recursive: true); } catch { /* best effort */ }
}

// ───────── helpers ─────────

void Step(string description)
{
    step++;
    Console.WriteLine($"[{step}] {description}");
}

void Log(string msg) => Console.WriteLine(msg);

void Assert(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}

async Task<string> RunOk(string arguments, string? stdin = null)
{
    var r = await Run(arguments, stdin);
    if (r.Code != 0)
    {
        throw new InvalidOperationException(
            $"brainz {arguments} exited {r.Code}\n--stdout--\n{r.Out}\n--stderr--\n{r.Err}");
    }
    return r.Out;
}

async Task<ProcResult> Run(string arguments, string? stdin = null)
{
    var psi = new ProcessStartInfo
    {
        FileName = binary,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = stdin is not null,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    psi.Environment["BRAINYZ_CONFIG_DIR"] = configDir;

    using var p = Process.Start(psi)
        ?? throw new InvalidOperationException($"failed to start {binary}");

    if (stdin is not null)
    {
        await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
    }

    var outTask = p.StandardOutput.ReadToEndAsync();
    var errTask = p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    return new ProcResult(
        p.ExitCode,
        (await outTask).TrimEnd('\r', '\n'),
        (await errTask).TrimEnd('\r', '\n'));
}

static async Task<string> PublishAsync(string repoRoot)
{
    var projectPath = Path.Combine(repoRoot, "src", "Brainyz.Cli", "Brainyz.Cli.csproj");
    var outputDir = Path.Combine(repoRoot, "tools", "smoke-binary");
    if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);

    var rid = CurrentRid();
    var psi = new ProcessStartInfo("dotnet")
    {
        ArgumentList =
        {
            "publish", projectPath,
            "-c", "Release",
            "-r", rid,
            "-p:PublishAot=false",
            "-p:PublishSingleFile=true",
            "--self-contained",
            "-o", outputDir,
            "--nologo",
        },
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("failed to launch dotnet publish");
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0)
    {
        var o = await proc.StandardOutput.ReadToEndAsync();
        var e = await proc.StandardError.ReadToEndAsync();
        throw new InvalidOperationException($"dotnet publish failed ({proc.ExitCode})\n{o}\n{e}");
    }

    var exe = OperatingSystem.IsWindows() ? "brainz.exe" : "brainz";
    var binary = Path.Combine(outputDir, exe);
    if (!File.Exists(binary))
        throw new FileNotFoundException($"published binary not found at {binary}");
    return binary;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "brainyz.slnx"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException(
        "brainyz.slnx not found — run this script from inside the repo.");
}

static string CurrentRid() =>
    (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, Architecture.X64)    => "win-x64",
        (true, _, Architecture.Arm64)  => "win-arm64",
        (_, true, Architecture.Arm64)  => "osx-arm64",
        (_, true, Architecture.X64)    => "osx-x64",
        (_, _, Architecture.X64)       => "linux-x64",
        (_, _, Architecture.Arm64)     => "linux-arm64",
        _ => throw new PlatformNotSupportedException(
            $"no RID mapping for {RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}"),
    };

internal readonly record struct ProcResult(int Code, string Out, string Err);
