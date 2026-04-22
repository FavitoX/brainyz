// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace Brainyz.Cli.Mcp;

/// <summary>
/// Source-generated serialization contract for the types returned by
/// <see cref="BrainTools"/>. Required because the MCP SDK uses AOT-safe JSON
/// (no reflection fallback); any type that crosses the tool boundary has to
/// be declared here. Chained with the SDK's own resolver in <c>McpCommand</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScopeInfo))]
[JsonSerializable(typeof(DecisionInfo))]
[JsonSerializable(typeof(PrincipleInfo))]
[JsonSerializable(typeof(NoteInfo))]
[JsonSerializable(typeof(ProjectInfo))]
[JsonSerializable(typeof(LinkInfo))]
[JsonSerializable(typeof(SearchHit))]
[JsonSerializable(typeof(EntityResult))]
[JsonSerializable(typeof(List<DecisionInfo>))]
[JsonSerializable(typeof(List<PrincipleInfo>))]
[JsonSerializable(typeof(List<NoteInfo>))]
[JsonSerializable(typeof(List<ProjectInfo>))]
[JsonSerializable(typeof(List<LinkInfo>))]
[JsonSerializable(typeof(List<SearchHit>))]
[JsonSerializable(typeof(string))]
internal sealed partial class BrainyzJsonContext : JsonSerializerContext;
