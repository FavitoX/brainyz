// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Models;
using ModelContextProtocol.Server;

namespace Brainyz.Cli.Mcp;

/// <summary>
/// MCP tool surface. Every method tagged <see cref="McpServerToolAttribute"/>
/// becomes a tool the agent can call. Tools map one-to-one with Core
/// operations and return wire-friendly DTOs (see <see cref="Dtos"/>).
/// </summary>
/// <remarks>
/// Registered with <c>WithTools&lt;BrainTools&gt;()</c> in <c>McpCommand</c>;
/// the DI container injects the shared <see cref="BrainContext"/>.
/// </remarks>
[McpServerToolType]
public sealed class BrainTools(BrainContext ctx)
{
    // ───────── Scope ─────────

    [McpServerTool]
    [Description("Resolve the project scope for a working directory using brainyz's precedence chain (.brain file → git remote → config path map → global). Use this to understand or debug why a query landed in a particular scope.")]
    public async Task<ScopeInfo> ResolveScope(
        [Description("Working directory to resolve. Defaults to the CLI process cwd.")] string? cwd = null,
        CancellationToken ct = default)
    {
        cwd ??= Directory.GetCurrentDirectory();
        var resolution = await ctx.Resolver.ResolveAsync(cwd, ct);
        return resolution.ToDto();
    }

    // ───────── Read ─────────

    [McpServerTool]
    [Description("Hybrid search across decisions, principles, and notes: fuses FTS5 text match with semantic (vector) similarity via Reciprocal Rank Fusion. Best default for open-ended questions. Falls back to FTS5 alone when embeddings are unavailable.")]
    public async Task<List<SearchHit>> Search(
        [Description("Natural language query. Keywords or a phrase.")] string query,
        [Description("Max rows in the final ranking. Default: 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        var hits = new List<SearchHit>();
        foreach (var h in await ctx.Search.SearchAsync(query, finalLimit: limit, ct: ct))
            hits.Add(new SearchHit(h.Type, h.Id, h.ProjectId, h.Title));
        return hits;
    }

    [McpServerTool]
    [Description("Semantic-only search (vector similarity, no text match). Useful when the user's wording probably differs from the stored text — e.g. 'retries' should surface a decision titled 'Polly circuit breaker policy'. Returns empty when Ollama is unreachable.")]
    public async Task<List<SearchHit>> FindSimilar(
        [Description("Natural language query describing the idea to find.")] string query,
        [Description("Max rows returned per entity type. Default: 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        var vector = await ctx.Embeddings.TryEmbedQueryAsync(query, ct);
        if (vector is null) return [];

        var hits = new List<SearchHit>();
        var model = ctx.EmbeddingConfig.Model;

        foreach (var (id, _) in await ctx.Store.SearchDecisionsByVectorAsync(vector, model, limit, ct))
        {
            var d = await ctx.Store.GetDecisionAsync(id, ct);
            if (d is not null) hits.Add(new SearchHit("decision", d.Id, d.ProjectId, d.Title));
        }
        foreach (var (id, _) in await ctx.Store.SearchPrinciplesByVectorAsync(vector, model, limit, ct))
        {
            var p = await ctx.Store.GetPrincipleAsync(id, ct);
            if (p is not null) hits.Add(new SearchHit("principle", p.Id, p.ProjectId, p.Title));
        }
        foreach (var (id, _) in await ctx.Store.SearchNotesByVectorAsync(vector, model, limit, ct))
        {
            var n = await ctx.Store.GetNoteAsync(id, ct);
            if (n is not null) hits.Add(new SearchHit("note", n.Id, n.ProjectId, n.Title));
        }
        return hits;
    }

    [McpServerTool]
    [Description("Fetch a decision, principle, or note by its id. The entity type is detected automatically. Exactly one of the typed fields is populated; the others are null.")]
    public async Task<EntityResult> Get(
        [Description("ULID of the entity (26 characters).")] string id,
        CancellationToken ct = default)
    {
        var type = await ctx.Store.FindEntityByIdAsync(id, ct);
        switch (type)
        {
            case LinkEntity.Decision:
                var d = await ctx.Store.GetDecisionAsync(id, ct);
                return new EntityResult("decision", d?.ToDto(), null, null);
            case LinkEntity.Principle:
                var p = await ctx.Store.GetPrincipleAsync(id, ct);
                return new EntityResult("principle", null, p?.ToDto(), null);
            case LinkEntity.Note:
                var n = await ctx.Store.GetNoteAsync(id, ct);
                return new EntityResult("note", null, null, n?.ToDto());
            default:
                return new EntityResult(null, null, null, null);
        }
    }

    [McpServerTool]
    [Description("List active principles for the resolved project scope plus all globals. Agents should call this early in a session so they honour user-defined rules (e.g. 'auth mandatory on every handler').")]
    public async Task<List<PrincipleInfo>> GetPrinciples(
        [Description("Working directory used to resolve the scope. Defaults to the CLI process cwd.")] string? cwd = null,
        [Description("Include global principles in addition to the project's. Default: true.")] bool includeGlobal = true,
        CancellationToken ct = default)
    {
        cwd ??= Directory.GetCurrentDirectory();
        var scope = await ctx.Resolver.ResolveAsync(cwd, ct);

        var result = new List<PrincipleInfo>();
        if (scope.ProjectId is not null)
        {
            foreach (var p in await ctx.Store.ListPrinciplesAsync(scope.ProjectId, activeOnly: true, ct))
                result.Add(p.ToDto());
        }
        if (includeGlobal || scope.ProjectId is null)
        {
            foreach (var p in await ctx.Store.ListPrinciplesAsync(projectId: null, activeOnly: true, ct))
                result.Add(p.ToDto());
        }
        return result;
    }

    [McpServerTool]
    [Description("List every registered project with its slug and remotes. Use when the agent needs to know which projects exist before adding or filtering.")]
    public async Task<List<ProjectInfo>> ListProjects(CancellationToken ct = default)
    {
        var result = new List<ProjectInfo>();
        foreach (var p in await ctx.Store.ListProjectsAsync(ct))
            result.Add(p.ToDto());
        return result;
    }

    [McpServerTool]
    [Description("List links pointing out of or into a given entity. Useful for navigating the knowledge graph (e.g. find everything a decision is informed by).")]
    public async Task<List<LinkInfo>> GetLinks(
        [Description("Optional filter: source entity id")] string? fromId = null,
        [Description("Optional filter: target entity id")] string? toId = null,
        [Description("Optional relation filter, e.g. supersedes / relates_to / depends_on / informed_by")] string? relation = null,
        CancellationToken ct = default)
    {
        LinkRelation? rel = null;
        if (!string.IsNullOrEmpty(relation))
        {
            if (!Mapping.TryParseRelation(relation, out var parsed))
                throw new ArgumentException($"Unknown relation '{relation}'");
            rel = parsed;
        }

        var links = await ctx.Store.GetLinksAsync(
            fromId: fromId, toId: toId, relation: rel, ct: ct);

        var result = new List<LinkInfo>();
        foreach (var l in links) result.Add(l.ToDto());
        return result;
    }

    // ───────── Write ─────────

    [McpServerTool]
    [Description("Record a new decision (a reasoned choice among alternatives). The minimum required inputs are title and text. When project is omitted the current resolved scope is used.")]
    public async Task<string> AddDecision(
        [Description("Short title of the decision.")] string title,
        [Description("Body: what was decided, in plain Markdown.")] string text,
        [Description("Problem or situation that led to the decision. Optional.")] string? context = null,
        [Description("Why this option was chosen. Optional.")] string? rationale = null,
        [Description("What this implies going forward. Optional.")] string? consequences = null,
        [Description("low | medium | high — how firm is the decision. Optional.")] string? confidence = null,
        [Description("proposed | accepted (default) | deprecated | superseded")] string? status = null,
        [Description("Project slug to scope to. Null uses the resolved scope.")] string? project = null,
        [Description("Working directory used for scope resolution when project is null.")] string? cwd = null,
        CancellationToken ct = default)
    {
        if (!Mapping.TryParseConfidence(confidence, out var conf))
            throw new ArgumentException($"Invalid confidence '{confidence}' (expected: low | medium | high)");
        if (!Mapping.TryParseStatus(status, out var st))
            throw new ArgumentException($"Invalid status '{status}' (expected: proposed | accepted | deprecated | superseded)");

        var projectId = await ResolveProjectAsync(project, cwd, ct);

        var decision = new Decision(
            Id: Ids.NewUlid(),
            ProjectId: projectId,
            Title: title,
            Text: text,
            Context: context,
            Rationale: rationale,
            Consequences: consequences,
            Status: st,
            Confidence: conf);

        await ctx.Store.AddDecisionAsync(decision, ct: ct);
        await ctx.Embeddings.IndexDecisionAsync(decision, ct);
        return decision.Id;
    }

    [McpServerTool]
    [Description("Record a new principle (a stable, prescriptive rule). Principles are exposed to agents at session start via GetPrinciples.")]
    public async Task<string> AddPrinciple(
        [Description("Short title of the principle.")] string title,
        [Description("The rule itself, written as an imperative statement.")] string statement,
        [Description("Why the rule applies. Optional.")] string? rationale = null,
        [Description("Project slug to scope to. Null uses the resolved scope.")] string? project = null,
        [Description("Working directory used for scope resolution when project is null.")] string? cwd = null,
        CancellationToken ct = default)
    {
        var projectId = await ResolveProjectAsync(project, cwd, ct);

        var principle = new Principle(
            Id: Ids.NewUlid(),
            ProjectId: projectId,
            Title: title,
            Statement: statement,
            Rationale: rationale);

        await ctx.Store.AddPrincipleAsync(principle, ct);
        await ctx.Embeddings.IndexPrincipleAsync(principle, ct);
        return principle.Id;
    }

    [McpServerTool]
    [Description("Record a new reference note (descriptive information, not prescriptive). Notes do not require rationale or alternatives.")]
    public async Task<string> AddNote(
        [Description("Short title of the note.")] string title,
        [Description("Body content in Markdown.")] string content,
        [Description("Optional URL or origin reference.")] string? source = null,
        [Description("Project slug to scope to. Null uses the resolved scope.")] string? project = null,
        [Description("Working directory used for scope resolution when project is null.")] string? cwd = null,
        CancellationToken ct = default)
    {
        var projectId = await ResolveProjectAsync(project, cwd, ct);

        var note = new Note(
            Id: Ids.NewUlid(),
            ProjectId: projectId,
            Title: title,
            Content: content,
            Source: source);

        await ctx.Store.AddNoteAsync(note, ct);
        await ctx.Embeddings.IndexNoteAsync(note, ct);
        return note.Id;
    }

    [McpServerTool]
    [Description("Patch specific fields of an existing decision. Fields left null stay as they are. Re-embeds the entity afterwards if the content changed.")]
    public async Task<string> UpdateDecision(
        [Description("ULID of the decision.")] string id,
        [Description("New title. Null keeps the existing title.")] string? title = null,
        [Description("New body. Null keeps the existing body.")] string? text = null,
        [Description("New context. Null keeps the existing context. Empty string clears.")] string? context = null,
        [Description("New rationale.")] string? rationale = null,
        [Description("New consequences.")] string? consequences = null,
        [Description("low | medium | high — pass 'none' to clear.")] string? confidence = null,
        [Description("proposed | accepted | deprecated | superseded")] string? status = null,
        CancellationToken ct = default)
    {
        var current = await ctx.Store.GetDecisionAsync(id, ct)
            ?? throw new ArgumentException($"Decision {id} not found");

        Confidence? conf = current.Confidence;
        if (confidence is not null)
        {
            if (confidence.Equals("none", StringComparison.OrdinalIgnoreCase)) conf = null;
            else if (Mapping.TryParseConfidence(confidence, out var parsed)) conf = parsed;
            else throw new ArgumentException($"Invalid confidence '{confidence}'");
        }

        DecisionStatus st = current.Status;
        if (status is not null)
        {
            if (!Mapping.TryParseStatus(status, out st))
                throw new ArgumentException($"Invalid status '{status}'");
        }

        var patched = current with
        {
            Title = title ?? current.Title,
            Text = text ?? current.Text,
            Context = context ?? current.Context,
            Rationale = rationale ?? current.Rationale,
            Consequences = consequences ?? current.Consequences,
            Confidence = conf,
            Status = st,
        };
        if (!await ctx.Store.UpdateDecisionAsync(patched, ct))
            throw new InvalidOperationException($"Update failed for decision {id}");
        await ctx.Embeddings.IndexDecisionAsync(patched, ct);
        return id;
    }

    [McpServerTool]
    [Description("Patch a principle. Pass only the fields you want to change.")]
    public async Task<string> UpdatePrinciple(
        [Description("ULID of the principle.")] string id,
        [Description("New title.")] string? title = null,
        [Description("New statement.")] string? statement = null,
        [Description("New rationale.")] string? rationale = null,
        [Description("Active flag. Null keeps the current value; false archives; true un-archives.")] bool? active = null,
        CancellationToken ct = default)
    {
        var current = await ctx.Store.GetPrincipleAsync(id, ct)
            ?? throw new ArgumentException($"Principle {id} not found");

        var patched = current with
        {
            Title = title ?? current.Title,
            Statement = statement ?? current.Statement,
            Rationale = rationale ?? current.Rationale,
            Active = active ?? current.Active,
        };
        if (!await ctx.Store.UpdatePrincipleAsync(patched, ct))
            throw new InvalidOperationException($"Update failed for principle {id}");
        await ctx.Embeddings.IndexPrincipleAsync(patched, ct);
        return id;
    }

    [McpServerTool]
    [Description("Patch a note. Pass only the fields you want to change.")]
    public async Task<string> UpdateNote(
        [Description("ULID of the note.")] string id,
        [Description("New title.")] string? title = null,
        [Description("New body content.")] string? content = null,
        [Description("New source URL.")] string? source = null,
        [Description("Active flag.")] bool? active = null,
        CancellationToken ct = default)
    {
        var current = await ctx.Store.GetNoteAsync(id, ct)
            ?? throw new ArgumentException($"Note {id} not found");

        var patched = current with
        {
            Title = title ?? current.Title,
            Content = content ?? current.Content,
            Source = source ?? current.Source,
            Active = active ?? current.Active,
        };
        if (!await ctx.Store.UpdateNoteAsync(patched, ct))
            throw new InvalidOperationException($"Update failed for note {id}");
        await ctx.Embeddings.IndexNoteAsync(patched, ct);
        return id;
    }

    [McpServerTool]
    [Description("Delete a decision, principle, or note by id. Schema triggers cascade to alternatives, embeddings, tag bindings and links, and keep a history snapshot. Returns true when a row was removed, false when the id did not match.")]
    public async Task<bool> Delete(
        [Description("ULID of the entity to delete.")] string id,
        CancellationToken ct = default)
    {
        var type = await ctx.Store.FindEntityByIdAsync(id, ct);
        return type switch
        {
            LinkEntity.Decision => await ctx.Store.DeleteDecisionAsync(id, ct),
            LinkEntity.Principle => await ctx.Store.DeletePrincipleAsync(id, ct),
            LinkEntity.Note => await ctx.Store.DeleteNoteAsync(id, ct),
            _ => false,
        };
    }

    [McpServerTool]
    [Description("Delete a link by its id. Entities on either end are not affected.")]
    public async Task<bool> DeleteLink(
        [Description("ULID of the link to delete.")] string linkId,
        CancellationToken ct = default)
    {
        return await ctx.Store.DeleteLinkAsync(linkId, ct);
    }

    [McpServerTool]
    [Description("Create a typed link between two entities (decision / principle / note). Types are detected from the ids.")]
    public async Task<string> Link(
        [Description("Source entity id (ULID).")] string fromId,
        [Description("One of: supersedes, relates_to, depends_on, conflicts_with, informed_by, derived_from, split_from, contradicts.")] string relation,
        [Description("Target entity id (ULID).")] string toId,
        [Description("Optional free-text annotation explaining the link.")] string? annotation = null,
        CancellationToken ct = default)
    {
        if (!Mapping.TryParseRelation(relation, out var rel))
            throw new ArgumentException($"Unknown relation '{relation}'");

        var fromType = await ctx.Store.FindEntityByIdAsync(fromId, ct)
            ?? throw new ArgumentException($"No entity with id {fromId}");
        var toType = await ctx.Store.FindEntityByIdAsync(toId, ct)
            ?? throw new ArgumentException($"No entity with id {toId}");

        var link = new Link(
            Id: Ids.NewUlid(),
            FromType: fromType,
            FromId: fromId,
            ToType: toType,
            ToId: toId,
            Relation: rel,
            Annotation: annotation);

        await ctx.Store.AddLinkAsync(link, ct);
        return link.Id;
    }

    // ───────── helpers ─────────

    private async Task<string?> ResolveProjectAsync(
        string? explicitSlug, string? cwd, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(explicitSlug))
        {
            var project = await ctx.Store.GetProjectBySlugAsync(explicitSlug, ct)
                ?? throw new ArgumentException($"Project '{explicitSlug}' not found");
            return project.Id;
        }

        cwd ??= Directory.GetCurrentDirectory();
        var scope = await ctx.Resolver.ResolveAsync(cwd, ct);
        return scope.ProjectId;
    }

    // ───────── Export / Backup (v0.3) ─────────

    [McpServerTool]
    [Description("Export the brainyz brain as a JSONL file. Useful for inspection, diffing, or additive re-import. Optionally filter by project id. Set includeHistory=true to also dump the audit trail (off by default). This is read-only from the DB perspective.")]
    public async Task<string> ExportBrain(
        [Description("Absolute path where the JSONL file should be written.")] string path,
        [Description("Optional project id to filter by; null or empty exports the whole brain.")] string? projectId = null,
        [Description("Include history entries (off by default).")] bool includeHistory = false,
        CancellationToken ct = default)
    {
        if (projectId is "") projectId = null;

        if (projectId is not null && await ctx.Store.GetProjectAsync(projectId, ct) is null)
            throw new BrainyzException(
                ErrorCode.BZ_EXPORT_PROJECT_NOT_FOUND,
                $"project '{projectId}' not found");

        var exporter = new JsonlExporter(ctx.Store, BrainyzCliVersion.Current);
        var result = await exporter.ExportAsync(path, projectId, includeHistory, ct);

        int total = 0;
        foreach (var v in result.Counts.Values) total += v;
        return $"exported {total} records to {path}";
    }
}
