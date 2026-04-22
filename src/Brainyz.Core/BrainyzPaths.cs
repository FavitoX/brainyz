// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core;

/// <summary>
/// Platform-specific filesystem locations for brainyz artifacts: the database,
/// the config file, and the parent directory. See D-002.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Linux: <c>$XDG_CONFIG_HOME/brainyz</c> or <c>~/.config/brainyz</c></item>
///   <item>macOS: <c>~/Library/Application Support/brainyz</c></item>
///   <item>Windows: <c>%APPDATA%\brainyz</c></item>
/// </list>
/// Tests inject a custom directory via the primary constructor.
/// </remarks>
public sealed record BrainyzPaths(string ConfigDir)
{
    public string DbPath => Path.Combine(ConfigDir, "brainyz.db");
    public string ConfigFile => Path.Combine(ConfigDir, "config.toml");

    public static BrainyzPaths Default()
    {
        // Explicit override always wins — useful for tests, CI, and power users
        // who want to keep brainyz state on a custom mount / removable drive.
        var overrideDir = Environment.GetEnvironmentVariable("BRAINYZ_CONFIG_DIR");
        if (!string.IsNullOrEmpty(overrideDir))
            return new BrainyzPaths(overrideDir);

        string configDir;
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            configDir = Path.Combine(appData, "brainyz");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(home, "Library", "Application Support", "brainyz");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            configDir = Path.Combine(baseDir, "brainyz");
        }
        return new BrainyzPaths(configDir);
    }
}
