// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.InteropServices;

namespace Brainyz.Cli;

/// <summary>
/// Workaround for Nelknet.LibSQL 0.2.5 failing to locate the libsql native
/// binary in single-file NativeAOT publishes. Nelknet's load path walks
/// <see cref="Assembly.Location"/>-derived locations which all collapse to
/// the empty string once the managed assembly is embedded in the exe; the
/// <see cref="AppDomain.CurrentDomain.BaseDirectory"/> fallback Nelknet
/// keeps is unreliable here too.
/// </summary>
/// <remarks>
/// <para>
/// Two safeguards, applied together so we don't need to know Nelknet's
/// internals precisely:
/// </para>
/// <list type="number">
///   <item>
///     <b>Pre-load via absolute path</b>. We compute <c>AppContext.BaseDirectory</c>
///     + <c>libsql.{dll,so,dylib}</c> and load it with
///     <see cref="NativeLibrary.Load(string)"/>. After that the module is in
///     the process and subsequent lookups for <c>libsql</c> find it by base
///     name.
///   </item>
///   <item>
///     <b>Register a <see cref="NativeLibrary.SetDllImportResolver"/></b> on
///     Nelknet's binding assembly so any residual <c>[DllImport("libsql")]</c>
///     resolution takes our already-loaded handle without hitting the broken
///     search paths.
///   </item>
/// </list>
/// <para>
/// Both are idempotent. If Nelknet ships an AOT-safe Initialize() later, this
/// class becomes a harmless no-op and can be deleted.
/// </para>
/// </remarks>
internal static class NativeLibraryResolver
{
    private static IntPtr _handle;

    public static void Register()
    {
        if (!TryPreload(out _handle)) return;

        // Any Nelknet type works to get the binding assembly; pick a public one.
        var nelknetAssembly = typeof(Nelknet.LibSQL.Data.LibSQLConnection).Assembly;
        NativeLibrary.SetDllImportResolver(nelknetAssembly, Resolve);
    }

    private static bool TryPreload(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle))
                return true;
        }
        return false;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Any request for the libsql binding reuses the handle we already
        // opened. Anything else falls back to the default resolution chain.
        return IsLibSql(libraryName) && _handle != IntPtr.Zero ? _handle : IntPtr.Zero;
    }

    private static bool IsLibSql(string name) =>
        name.Equals("libsql", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libsql.", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Candidates()
    {
        var baseDir = AppContext.BaseDirectory;

        string libraryFile = OperatingSystem.IsWindows() ? "libsql.dll"
                           : OperatingSystem.IsMacOS()   ? "libsql.dylib"
                           :                               "libsql.so";

        // Shipped at the archive root by our release packaging.
        yield return Path.Combine(baseDir, libraryFile);

        // Fallback for `dotnet publish` outputs that preserve runtime layout.
        var rid = RuntimeInformation.RuntimeIdentifier;
        yield return Path.Combine(baseDir, "runtimes", rid, "native", libraryFile);
    }
}
