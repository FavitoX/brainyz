// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;

namespace Brainyz.Core.Embeddings;

/// <summary>
/// Deterministic hash of the text we'd embed for a given entity. Used as the
/// cache key in the <c>*_embeddings.content_hash</c> column: when we re-index
/// an entity, if the hash already matches the stored one, we can skip the
/// Ollama call entirely.
/// </summary>
public static class ContentHash
{
    /// <summary>
    /// Short 16-char truncation of SHA-256 (64 bits of hash space), enough to
    /// detect accidental rewrites without paying for 64-char keys in the DB.
    /// </summary>
    public static string Of(string text)
    {
        Span<byte> bytes = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), bytes);
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
