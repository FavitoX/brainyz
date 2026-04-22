// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Embeddings;
using Brainyz.Core.Scope;
using Brainyz.Core.Search;
using Brainyz.Core.Storage;

namespace Brainyz.Cli;

/// <summary>
/// Shared plumbing every command needs: resolved paths, an open
/// <see cref="BrainStore"/>, a configured <see cref="ScopeResolver"/>, and
/// an optional <see cref="EmbeddingService"/> backed by a lazy Ollama
/// client. Opening is idempotent — first call creates the config directory
/// and initializes the database schema if missing.
/// </summary>
public sealed class BrainContext : IAsyncDisposable
{
    public BrainyzPaths Paths { get; }
    public BrainStore Store { get; }
    public ScopeResolver Resolver { get; }
    public EmbeddingConfig EmbeddingConfig { get; }
    public EmbeddingService Embeddings { get; }
    public HybridSearch Search { get; }

    private readonly OllamaClient _ollama;

    private BrainContext(
        BrainyzPaths paths,
        BrainStore store,
        ScopeResolver resolver,
        EmbeddingConfig config,
        OllamaClient ollama,
        EmbeddingService embeddings,
        HybridSearch search)
    {
        Paths = paths;
        Store = store;
        Resolver = resolver;
        EmbeddingConfig = config;
        _ollama = ollama;
        Embeddings = embeddings;
        Search = search;
    }

    public static async Task<BrainContext> OpenAsync(CancellationToken ct = default)
    {
        var paths = BrainyzPaths.Default();
        Directory.CreateDirectory(paths.ConfigDir);
        var store = await BrainStore.OpenAsync(paths.DbPath, ct: ct);
        var resolver = new ScopeResolver(store, paths);

        var config = EmbeddingConfig.FromEnvironment();
        var ollama = OllamaClient.Create(config);
        var embeddings = new EmbeddingService(store, ollama, config);
        var search = new HybridSearch(store, embeddings);

        return new BrainContext(paths, store, resolver, config, ollama, embeddings, search);
    }

    public async ValueTask DisposeAsync()
    {
        _ollama.Dispose();
        await Store.DisposeAsync();
    }
}
