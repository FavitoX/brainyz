// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Embeddings;

/// <summary>
/// Configuration for the embedding provider. Defaults match D-017:
/// nomic-embed-text v1.5 via Ollama, dimension 768.
/// </summary>
/// <param name="Host">Base URL of the Ollama HTTP API, no trailing slash.</param>
/// <param name="Model">Model identifier passed to Ollama.</param>
/// <param name="Dim">Expected vector dimension; must match the DB <c>F32_BLOB(N)</c> columns.</param>
/// <param name="RequestTimeout">Timeout for a single embedding request.</param>
public sealed record EmbeddingConfig(
    string Host = "http://localhost:11434",
    string Model = "nomic-embed-text:v1.5",
    int Dim = 768,
    TimeSpan? RequestTimeout = null)
{
    public TimeSpan EffectiveTimeout => RequestTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>
    /// Builds a config from environment variables, falling back to defaults.
    /// Honours <c>BRAINYZ_OLLAMA_HOST</c> and <c>BRAINYZ_EMBEDDING_MODEL</c>.
    /// </summary>
    public static EmbeddingConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("BRAINYZ_OLLAMA_HOST");
        var model = Environment.GetEnvironmentVariable("BRAINYZ_EMBEDDING_MODEL");
        return new EmbeddingConfig(
            Host: !string.IsNullOrEmpty(host) ? host.TrimEnd('/') : "http://localhost:11434",
            Model: !string.IsNullOrEmpty(model) ? model : "nomic-embed-text:v1.5",
            Dim: 768);
    }
}
