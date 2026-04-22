// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Cli;

/// <summary>
/// Resolves a body-of-text argument from either an inline <c>--text</c> flag
/// or piped stdin. Never blocks: when stdin is an interactive TTY we don't
/// read from it.
/// </summary>
public static class InputText
{
    public static async Task<string?> ResolveAsync(string? inline, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(inline)) return inline;
        if (!Console.IsInputRedirected) return null;

        var text = await Console.In.ReadToEndAsync(ct);
        text = text.TrimEnd('\r', '\n', ' ', '\t');
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
