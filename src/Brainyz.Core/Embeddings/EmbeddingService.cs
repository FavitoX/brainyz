// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;
using Brainyz.Core.Storage;

namespace Brainyz.Core.Embeddings;

/// <summary>
/// Generates embeddings via <see cref="OllamaClient"/> and persists them
/// through <see cref="BrainStore"/>. Exposes "best-effort" index methods
/// that never throw on transport failure (Ollama not running, timeout, …):
/// the entity stays queryable via FTS5 and can be reindexed later.
/// </summary>
public sealed class EmbeddingService(
    BrainStore store,
    OllamaClient ollama,
    EmbeddingConfig config)
{
    public EmbeddingConfig Config { get; } = config;

    public async Task<IndexResult> IndexDecisionAsync(Decision d, CancellationToken ct = default)
    {
        var text = BuildDecisionText(d);
        var hash = ContentHash.Of(text);
        if (await store.GetDecisionContentHashAsync(d.Id, Config.Model, ct) == hash)
            return IndexResult.UpToDate;

        var embed = await TryEmbedAsync(text, ct);
        if (embed is null) return IndexResult.ProviderUnavailable;

        await store.UpsertDecisionEmbeddingAsync(d.Id, Config.Model, embed, hash, ct);
        return IndexResult.Indexed;
    }

    public async Task<IndexResult> IndexPrincipleAsync(Principle p, CancellationToken ct = default)
    {
        var text = BuildPrincipleText(p);
        var hash = ContentHash.Of(text);
        if (await store.GetPrincipleContentHashAsync(p.Id, Config.Model, ct) == hash)
            return IndexResult.UpToDate;

        var embed = await TryEmbedAsync(text, ct);
        if (embed is null) return IndexResult.ProviderUnavailable;

        await store.UpsertPrincipleEmbeddingAsync(p.Id, Config.Model, embed, hash, ct);
        return IndexResult.Indexed;
    }

    public async Task<IndexResult> IndexNoteAsync(Note n, CancellationToken ct = default)
    {
        var text = BuildNoteText(n);
        var hash = ContentHash.Of(text);
        if (await store.GetNoteContentHashAsync(n.Id, Config.Model, ct) == hash)
            return IndexResult.UpToDate;

        var embed = await TryEmbedAsync(text, ct);
        if (embed is null) return IndexResult.ProviderUnavailable;

        await store.UpsertNoteEmbeddingAsync(n.Id, Config.Model, embed, hash, ct);
        return IndexResult.Indexed;
    }

    /// <summary>
    /// Returns an embedding for the query text or null if the provider is
    /// unreachable. Callers combine this with a vector search on the store
    /// when doing semantic or hybrid queries.
    /// </summary>
    public Task<float[]?> TryEmbedQueryAsync(string text, CancellationToken ct = default)
        => TryEmbedAsync(text, ct);

    // ───────── internals ─────────

    private async Task<float[]?> TryEmbedAsync(string text, CancellationToken ct)
    {
        try
        {
            return await ollama.EmbedAsync(text, ct);
        }
        catch (HttpRequestException)
        {
            return null; // Ollama not running / unreachable
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // request timeout, not user-initiated cancel
        }
    }

    public static string BuildDecisionText(Decision d) => Compose(
        d.Title,
        d.Context,
        d.Text,
        d.Rationale,
        d.Consequences);

    public static string BuildPrincipleText(Principle p) => Compose(
        p.Title,
        p.Statement,
        p.Rationale);

    public static string BuildNoteText(Note n) => Compose(
        n.Title,
        n.Content);

    private static string Compose(params string?[] parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(part.Trim());
        }
        return sb.ToString();
    }
}

public enum IndexResult
{
    /// <summary>The vector was written or replaced in the store.</summary>
    Indexed,

    /// <summary>
    /// The stored hash already matched the computed hash — no network call
    /// was made and no row was modified.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Ollama was unreachable or timed out. The entity stays unembedded;
    /// a later <c>reindex</c> can recover.
    /// </summary>
    ProviderUnavailable,
}
