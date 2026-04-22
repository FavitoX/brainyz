// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Embeddings;
using Brainyz.Core.Models;
using Brainyz.Core.Storage;

namespace Brainyz.Core.Search;

/// <summary>
/// Hybrid search: fuses FTS5 (text match) and vector (semantic) results via
/// Reciprocal Rank Fusion so each entity scored by an independent signal
/// contributes to the final ranking. RRF is robust across sources without
/// needing per-source score normalisation (D-009).
/// </summary>
/// <remarks>
/// Gracefully degrades when embeddings are unavailable: if
/// <see cref="EmbeddingService.TryEmbedQueryAsync"/> returns null the final
/// ranking collapses to FTS5 alone.
/// </remarks>
public sealed class HybridSearch(BrainStore store, EmbeddingService? embeddings = null)
{
    private const double RrfK = 60.0;

    public record Hit(string Type, string Id, string? ProjectId, string Title, double Score);

    public async Task<IReadOnlyList<Hit>> SearchAsync(
        string query,
        int perTypeLimit = 20,
        int finalLimit = 20,
        CancellationToken ct = default)
    {
        var hits = new List<Hit>();

        var ftsDecisions = await store.SearchDecisionsAsync(query, perTypeLimit, ct);
        var ftsPrinciples = await store.SearchPrinciplesAsync(query, perTypeLimit, ct);
        var ftsNotes = await store.SearchNotesAsync(query, perTypeLimit, ct);

        float[]? queryVector = embeddings is null ? null : await embeddings.TryEmbedQueryAsync(query, ct);
        IReadOnlyList<(string, double)> vecDec = [], vecPr = [], vecNt = [];
        if (queryVector is not null && embeddings is not null)
        {
            vecDec = await store.SearchDecisionsByVectorAsync(queryVector, embeddings.Config.Model, perTypeLimit, ct);
            vecPr  = await store.SearchPrinciplesByVectorAsync(queryVector, embeddings.Config.Model, perTypeLimit, ct);
            vecNt  = await store.SearchNotesByVectorAsync(queryVector, embeddings.Config.Model, perTypeLimit, ct);
        }

        hits.AddRange(Fuse("decision", ftsDecisions.Select(d => (d.Id, d.ProjectId, d.Title)), vecDec));
        hits.AddRange(Fuse("principle", ftsPrinciples.Select(p => (p.Id, p.ProjectId, p.Title)), vecPr));
        hits.AddRange(Fuse("note", ftsNotes.Select(n => (n.Id, n.ProjectId, n.Title)), vecNt));

        return hits
            .OrderByDescending(h => h.Score)
            .Take(finalLimit)
            .ToList();
    }

    // RRF per source, then sum. One entity may appear in both FTS and vector
    // ranks with different positions; we credit it once per source.
    private static IEnumerable<Hit> Fuse(
        string type,
        IEnumerable<(string Id, string? ProjectId, string Title)> ftsRanked,
        IReadOnlyList<(string EntityId, double Distance)> vecRanked)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var meta = new Dictionary<string, (string?, string)>(StringComparer.Ordinal);

        int rank = 0;
        foreach (var (id, projectId, title) in ftsRanked)
        {
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (RrfK + rank);
            meta[id] = (projectId, title);
            rank++;
        }

        rank = 0;
        foreach (var (id, _) in vecRanked)
        {
            scores[id] = scores.GetValueOrDefault(id) + 1.0 / (RrfK + rank);
            meta.TryAdd(id, (null, string.Empty)); // title may be filled by FTS entry; otherwise blank
            rank++;
        }

        foreach (var kv in scores)
        {
            var (projectId, title) = meta[kv.Key];
            yield return new Hit(type, kv.Key, projectId, title, kv.Value);
        }
    }
}
