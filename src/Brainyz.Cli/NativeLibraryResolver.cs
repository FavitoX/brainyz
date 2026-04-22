// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
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
    private static readonly bool Diag =
        Environment.GetEnvironmentVariable("BRAINYZ_DIAG") is "1" or "true";

    private static IntPtr _handle;

    public static void Register()
    {
        if (Diag)
        {
            Console.Error.WriteLine($"[diag] AppContext.BaseDirectory = {AppContext.BaseDirectory}");
            Console.Error.WriteLine($"[diag] AppDomain.CurrentDomain.BaseDirectory = {AppDomain.CurrentDomain.BaseDirectory}");
            Console.Error.WriteLine($"[diag] Environment.CurrentDirectory = {Environment.CurrentDirectory}");
            Console.Error.WriteLine($"[diag] Environment.ProcessPath = {Environment.ProcessPath}");
        }

        if (!TryPreload(out _handle))
        {
            if (Diag) Console.Error.WriteLine("[diag] preload failed for every candidate; resolver not registered.");
            return;
        }

        if (Diag) Console.Error.WriteLine($"[diag] preload ok; handle=0x{_handle:X}");

        // Force-load Nelknet.LibSQL.Bindings before scanning — otherwise the
        // assembly hasn't been resolved yet (it's a transitive dependency of
        // Data that the CLR only loads when one of its members is used).
        Assembly? bindingsAssembly = null;
        try { bindingsAssembly = Assembly.Load("Nelknet.LibSQL.Bindings"); }
        catch (Exception ex) when (Diag)
        {
            Console.Error.WriteLine($"[diag] Assembly.Load(\"Nelknet.LibSQL.Bindings\") threw {ex.GetType().Name}: {ex.Message}");
        }

        // Register on both assemblies so any P/Invoke for "libsql" from
        // either surface hits our pre-loaded handle.
        var dataAssembly = typeof(Nelknet.LibSQL.Data.LibSQLConnection).Assembly;
        NativeLibrary.SetDllImportResolver(dataAssembly, Resolve);
        if (bindingsAssembly is not null)
            NativeLibrary.SetDllImportResolver(bindingsAssembly, Resolve);
        if (Diag) Console.Error.WriteLine($"[diag] resolver registered on {dataAssembly.GetName().Name}" +
                                          (bindingsAssembly is not null ? " and Nelknet.LibSQL.Bindings" : ""));

        // Nelknet.LibSQL.Bindings.LibSQLNativeLibrary.TryInitialize() derives
        // its search paths from Assembly.Location, which is empty in
        // single-file bundles. Its AppDomain.CurrentDomain.BaseDirectory
        // fallback does not recover either (observed on .NET 10 single-file
        // Windows x64). Since we just loaded the native lib successfully,
        // flip Nelknet's private _isInitialized flag so it skips its broken
        // search and lets the subsequent P/Invokes bind to the already-
        // loaded module.
        ShortCircuitNelknetInitialization(bindingsAssembly);
    }

    // Tell the IL trimmer to keep LibSQLNativeLibrary and its _isInitialized
    // field around even in aggressive AOT. Without this the flag is eligible
    // for removal (private static, no code that the analyzer can see uses it)
    // and the reflection lookup below fails silently.
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.NonPublicFields,
        "Nelknet.LibSQL.Bindings.LibSQLNativeLibrary",
        "Nelknet.LibSQL.Bindings")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Target is preserved via DynamicDependency on the same method.")]
    private static void ShortCircuitNelknetInitialization(Assembly? bindingsAssembly)
    {
        _ = bindingsAssembly; // kept for diagnostic symmetry; resolution uses AQN below

        try
        {
            // Type.GetType with a string-literal assembly-qualified name is
            // the trim-safe form (warns if we used Assembly.GetType(string)).
            var type = Type.GetType(
                "Nelknet.LibSQL.Bindings.LibSQLNativeLibrary, Nelknet.LibSQL.Bindings");
            var field = type?.GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Static);
            if (field is not null && field.FieldType == typeof(bool))
            {
                field.SetValue(null, true);
                if (Diag) Console.Error.WriteLine("[diag] short-circuited Nelknet._isInitialized = true");
            }
            else if (Diag)
            {
                Console.Error.WriteLine(
                    $"[diag] could not locate LibSQLNativeLibrary._isInitialized " +
                    $"(type={type?.FullName ?? "null"}, field={field?.Name ?? "null"}).");
            }
        }
        catch (Exception ex)
        {
            if (Diag) Console.Error.WriteLine($"[diag] reflection hack threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryPreload(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var candidate in Candidates())
        {
            var exists = File.Exists(candidate);
            if (Diag) Console.Error.WriteLine($"[diag] candidate {candidate} exists={exists}");
            if (!exists) continue;

            try
            {
                handle = NativeLibrary.Load(candidate);
                return true;
            }
            catch (Exception ex)
            {
                if (Diag) Console.Error.WriteLine($"[diag] Load({candidate}) threw {ex.GetType().Name}: {ex.Message}");
            }
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
