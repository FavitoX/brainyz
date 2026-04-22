// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core;

/// <summary>
/// Injectable clock. Timestamps are expressed in epoch milliseconds
/// (portable, sortable, and directly compatible with SQLite INTEGER columns).
/// </summary>
public interface IClock
{
    long NowMs();
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
