// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

/// <summary>
/// Tests the vector columns end-to-end against a real LibSQL instance:
/// upsert + content-hash check + cosine-distance query. No Ollama required —
/// we pass synthetic float[] vectors straight to the store.
/// </summary>
public sealed class EmbeddingsStorageTests : StoreTestBase
{
    private const string Model = "test-model:v1";

    [Fact]
    public async Task Upsert_then_get_content_hash_returns_the_written_hash()
    {
        var d = new Decision(Ids.NewUlid(), null, "t", "body");
        await Store.AddDecisionAsync(d);

        var vec = new float[768];
        for (int i = 0; i < vec.Length; i++) vec[i] = i * 0.001f;

        await Store.UpsertDecisionEmbeddingAsync(d.Id, Model, vec, contentHash: "abc123", ct: default);

        var stored = await Store.GetDecisionContentHashAsync(d.Id, Model);
        Assert.Equal("abc123", stored);
    }

    [Fact]
    public async Task Upsert_replaces_previous_vector_for_same_entity_and_model()
    {
        var d = new Decision(Ids.NewUlid(), null, "t", "body");
        await Store.AddDecisionAsync(d);

        var v1 = FakeVector(0.1f);
        var v2 = FakeVector(0.9f);

        await Store.UpsertDecisionEmbeddingAsync(d.Id, Model, v1, "hash-v1");
        await Store.UpsertDecisionEmbeddingAsync(d.Id, Model, v2, "hash-v2");

        Assert.Equal("hash-v2", await Store.GetDecisionContentHashAsync(d.Id, Model));
    }

    [Fact]
    public async Task Vector_search_returns_the_nearest_entity_first()
    {
        var d1 = new Decision(Ids.NewUlid(), null, "closer", "body");
        var d2 = new Decision(Ids.NewUlid(), null, "farther", "body");
        await Store.AddDecisionAsync(d1);
        await Store.AddDecisionAsync(d2);

        var near = FakeVector(0.1f);
        var far = FakeVector(-0.9f);
        var query = FakeVector(0.1f);

        await Store.UpsertDecisionEmbeddingAsync(d1.Id, Model, near, "h1");
        await Store.UpsertDecisionEmbeddingAsync(d2.Id, Model, far, "h2");

        var results = await Store.SearchDecisionsByVectorAsync(query, Model, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(d1.Id, results[0].EntityId);
        Assert.True(results[0].Distance <= results[1].Distance,
            $"nearest first expected: d1 dist={results[0].Distance}, d2 dist={results[1].Distance}");
    }

    [Fact]
    public async Task Principles_and_notes_also_support_upsert_and_vector_search()
    {
        var p = new Principle(Ids.NewUlid(), null, "Simple", "One file");
        var n = new Note(Ids.NewUlid(), null, "Stripe", "content");
        await Store.AddPrincipleAsync(p);
        await Store.AddNoteAsync(n);

        var vec = FakeVector(0.2f);

        await Store.UpsertPrincipleEmbeddingAsync(p.Id, Model, vec, "ph");
        await Store.UpsertNoteEmbeddingAsync(n.Id, Model, vec, "nh");

        Assert.Equal("ph", await Store.GetPrincipleContentHashAsync(p.Id, Model));
        Assert.Equal("nh", await Store.GetNoteContentHashAsync(n.Id, Model));

        Assert.Single(await Store.SearchPrinciplesByVectorAsync(vec, Model));
        Assert.Single(await Store.SearchNotesByVectorAsync(vec, Model));
    }

    private static float[] FakeVector(float fill)
    {
        var v = new float[768];
        for (int i = 0; i < v.Length; i++) v[i] = fill + i * 1e-5f;
        return v;
    }
}
