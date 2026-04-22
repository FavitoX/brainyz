// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class DecisionsTests : StoreTestBase
{
    [Fact]
    public async Task AddDecision_with_alternatives_persists_both_rows()
    {
        var project = new Project(Ids.NewUlid(), "brainyz", "brainyz");
        await Store.AddProjectAsync(project);

        var decision = new Decision(
            Id: Ids.NewUlid(),
            ProjectId: project.Id,
            Title: "Use LibSQL",
            Text: "Embedded LibSQL with native vectors",
            Context: "We need embeddings and multi-device sync",
            Rationale: "LibSQL is a SQLite superset that covers both needs",
            Consequences: "SQLite-compatible tooling still works",
            Status: DecisionStatus.Accepted,
            Confidence: Confidence.High);

        var alternatives = new[]
        {
            new Alternative(Ids.NewUlid(), decision.Id, "Plain SQLite",
                "Would require external extensions for native vectors"),
            new Alternative(Ids.NewUlid(), decision.Id, "DuckDB",
                "OLAP-oriented, not the right fit for our transactional workload"),
        };

        await Store.AddDecisionAsync(decision, alternatives);

        var found = await Store.GetDecisionAsync(decision.Id);
        Assert.NotNull(found);
        Assert.Equal(decision.Title, found!.Title);
        Assert.Equal(decision.Text, found.Text);
        Assert.Equal(DecisionStatus.Accepted, found.Status);
        Assert.Equal(Confidence.High, found.Confidence);
        Assert.Equal(project.Id, found.ProjectId);

        var storedAlts = await Store.GetAlternativesAsync(decision.Id);
        Assert.Equal(2, storedAlts.Count);
        Assert.Contains(storedAlts, a => a.Title == "Plain SQLite");
        Assert.Contains(storedAlts, a => a.Title == "DuckDB");
    }

    [Fact]
    public async Task SearchDecisions_finds_rows_by_FTS5_on_title_and_body()
    {
        var d1 = new Decision(Ids.NewUlid(), null,
            "Use LibSQL", "Embedded LibSQL with native vectors");
        var d2 = new Decision(Ids.NewUlid(), null,
            "Markdown content", "All content stored in TEXT columns");

        await Store.AddDecisionAsync(d1);
        await Store.AddDecisionAsync(d2);

        var hits = await Store.SearchDecisionsAsync("LibSQL");
        Assert.Single(hits);
        Assert.Equal(d1.Id, hits[0].Id);

        var hitsMarkdown = await Store.SearchDecisionsAsync("Markdown");
        Assert.Single(hitsMarkdown);
        Assert.Equal(d2.Id, hitsMarkdown[0].Id);
    }

    [Fact]
    public async Task GetDecision_returns_null_when_id_does_not_exist()
    {
        var found = await Store.GetDecisionAsync(Ids.NewUlid());
        Assert.Null(found);
    }
}
