// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Errors;

/// <summary>
/// Typed exception carrying a stable <see cref="ErrorCode"/>. The CLI
/// top-level handler formats the code, message, and tip into the
/// user-facing <c>Error [BZ_*]: ...</c> output (see §7.3 of the spec).
/// </summary>
public sealed class BrainyzException : Exception
{
    public ErrorCode Code { get; }

    /// <summary>
    /// Optional remediation hint. Shown to the user on a <c>Tip:</c> line.
    /// </summary>
    public string? Tip { get; }

    public BrainyzException(
        ErrorCode code,
        string message,
        string? tip = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Tip = tip;
    }
}
