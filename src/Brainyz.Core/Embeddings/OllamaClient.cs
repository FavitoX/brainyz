// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Brainyz.Core.Embeddings;

/// <summary>
/// Minimal HTTP wrapper around Ollama's <c>POST /api/embeddings</c>. The
/// endpoint accepts <c>{ "model", "prompt" }</c> and returns
/// <c>{ "embedding": [float, …] }</c>.
/// </summary>
/// <remarks>
/// Deliberately thin. No retries, no caching — embedding is used as a best-
/// effort hook on writes and a cache-hash check avoids redundant calls anyway.
/// If Ollama is unreachable the caller decides whether to log-and-skip or
/// propagate.
/// </remarks>
public sealed class OllamaClient(HttpClient http, EmbeddingConfig config) : IDisposable
{
    private readonly HttpClient _http = http;
    private readonly EmbeddingConfig _config = config;

    public static OllamaClient Create(EmbeddingConfig config)
    {
        var http = new HttpClient { BaseAddress = new Uri(config.Host), Timeout = config.EffectiveTimeout };
        return new OllamaClient(http, config);
    }

    /// <summary>
    /// Requests an embedding for <paramref name="text"/>. Throws
    /// <see cref="HttpRequestException"/> on network or HTTP errors and
    /// <see cref="InvalidDataException"/> when the response shape is wrong
    /// or the returned vector's length differs from <c>config.Dim</c>.
    /// </summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = new EmbeddingRequest(_config.Model, text);
        using var response = await _http.PostAsJsonAsync(
            "/api/embeddings", request, EmbeddingsJsonContext.Default.EmbeddingRequest, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync(
            EmbeddingsJsonContext.Default.EmbeddingResponse, ct)
            ?? throw new InvalidDataException("Ollama returned an empty body.");

        if (body.Embedding is null || body.Embedding.Length == 0)
            throw new InvalidDataException("Ollama response has no 'embedding' field.");

        if (body.Embedding.Length != _config.Dim)
            throw new InvalidDataException(
                $"Ollama returned a {body.Embedding.Length}-dim vector but the store expects {_config.Dim}.");

        return body.Embedding;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record EmbeddingRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("prompt")] string Prompt);

internal sealed record EmbeddingResponse(
    [property: JsonPropertyName("embedding")] float[]? Embedding);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
internal sealed partial class EmbeddingsJsonContext : JsonSerializerContext;
