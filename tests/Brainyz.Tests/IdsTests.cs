// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;

namespace Brainyz.Tests;

public sealed class IdsTests
{
    [Fact]
    public void NewUlid_returns_26_chars_and_is_time_sortable()
    {
        var a = Ids.NewUlid();
        Thread.Sleep(2);
        var b = Ids.NewUlid();

        Assert.Equal(26, a.Length);
        Assert.Equal(26, b.Length);
        Assert.True(string.CompareOrdinal(a, b) < 0,
            $"ULIDs should sort by creation time: {a} < {b}");
    }
}
