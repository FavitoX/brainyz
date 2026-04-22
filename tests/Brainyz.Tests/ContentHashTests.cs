// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Embeddings;

namespace Brainyz.Tests;

public sealed class ContentHashTests
{
    [Fact]
    public void Hash_is_16_hex_chars()
    {
        var h = ContentHash.Of("some text");
        Assert.Equal(16, h.Length);
        Assert.Matches("^[0-9a-f]{16}$", h);
    }

    [Fact]
    public void Identical_inputs_produce_identical_hashes()
    {
        Assert.Equal(
            ContentHash.Of("foo\n\nbar"),
            ContentHash.Of("foo\n\nbar"));
    }

    [Fact]
    public void Different_inputs_produce_different_hashes()
    {
        Assert.NotEqual(
            ContentHash.Of("foo"),
            ContentHash.Of("bar"));
    }
}
