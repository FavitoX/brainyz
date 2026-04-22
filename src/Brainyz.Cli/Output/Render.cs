// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;
using Brainyz.Core.Scope;

namespace Brainyz.Cli.Output;

/// <summary>
/// Plain-text renderers for CLI output. Kept intentionally minimal: no ANSI
/// colours, no table drawing — just aligned columns and headers so the output
/// composes cleanly with <c>grep</c>, <c>awk</c> and friends.
/// </summary>
public static class Render
{
    public static void ScopeLine(ScopeResolution s)
    {
        Console.WriteLine($"source : {SourceLabel(s.Source)}");
        Console.WriteLine($"project: {s.ProjectId ?? "(global)"}{(s.ProjectSlug is null ? "" : $"  {s.ProjectSlug}")}");
        if (s.Hint is not null) Console.WriteLine($"hint   : {s.Hint}");
    }

    public static void Decision(Decision d)
    {
        WriteField("id", d.Id);
        WriteField("project", d.ProjectId ?? "(global)");
        WriteField("title", d.Title);
        WriteField("status", d.Status.ToString().ToLowerInvariant());
        if (d.Confidence is { } c) WriteField("confidence", c.ToString().ToLowerInvariant());
        Section("context",      d.Context);
        Section("decision",     d.Text);
        Section("rationale",    d.Rationale);
        Section("consequences", d.Consequences);
    }

    public static void Principle(Principle p)
    {
        WriteField("id", p.Id);
        WriteField("project", p.ProjectId ?? "(global)");
        WriteField("title", p.Title);
        WriteField("active", p.Active ? "yes" : "no");
        Section("statement", p.Statement);
        Section("rationale", p.Rationale);
    }

    public static void Note(Note n)
    {
        WriteField("id", n.Id);
        WriteField("project", n.ProjectId ?? "(global)");
        WriteField("title", n.Title);
        WriteField("active", n.Active ? "yes" : "no");
        if (n.Source is not null) WriteField("source", n.Source);
        Section("content", n.Content);
    }

    public static void DecisionRow(Decision d) =>
        Console.WriteLine($"{d.Id}  {(d.ProjectId ?? "(global)"),-28}  {Truncate(d.Title, 60)}");

    public static void PrincipleRow(Principle p) =>
        Console.WriteLine($"{p.Id}  {(p.ProjectId ?? "(global)"),-28}  {Truncate(p.Title, 60)}");

    public static void NoteRow(Note n) =>
        Console.WriteLine($"{n.Id}  {(n.ProjectId ?? "(global)"),-28}  {Truncate(n.Title, 60)}");

    public static void ProjectRow(Project p) =>
        Console.WriteLine($"{p.Id}  {p.Slug,-20}  {p.Name}");

    public static void SearchHit(string type, string id, string? projectId, string title) =>
        Console.WriteLine($"{type,-9}  {id}  {(projectId ?? "(global)"),-28}  {Truncate(title, 60)}");

    private static void WriteField(string key, string value)
        => Console.WriteLine($"{key,-11}: {value}");

    private static void Section(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Console.WriteLine();
        Console.WriteLine($"── {label} ──");
        Console.WriteLine(value);
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";

    private static string SourceLabel(ScopeSource source) => source switch
    {
        ScopeSource.BrainFile => "brain-file",
        ScopeSource.GitRemote => "git-remote",
        ScopeSource.ConfigPathMap => "config-path-map",
        ScopeSource.Global => "global",
        _ => "unknown"
    };
}
