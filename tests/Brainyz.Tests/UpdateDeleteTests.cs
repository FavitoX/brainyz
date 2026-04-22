// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

/// <summary>
/// End-to-end coverage of the mutation API for every entity: update replaces
/// fields, delete removes rows and cascades through the schema triggers
/// (alternatives, embeddings, tag bindings, history, incoming/outgoing links).
/// </summary>
public sealed class UpdateDeleteTests : StoreTestBase
{
    [Fact]
    public async Task UpdateDecision_replaces_fields_and_bumps_updated_at()
    {
        var d = new Decision(Ids.NewUlid(), null, "Old title", "Old body",
            Status: DecisionStatus.Proposed, Confidence: Confidence.Low);
        await Store.AddDecisionAsync(d);

        var original = await Store.GetDecisionAsync(d.Id);
        Assert.NotNull(original);

        var updated = original! with
        {
            Title = "New title",
            Text = "New body",
            Status = DecisionStatus.Accepted,
            Confidence = Confidence.High,
            Rationale = "Because of X",
        };
        await Task.Delay(5); // ensure updated_at monotonically advances
        var ok = await Store.UpdateDecisionAsync(updated);
        Assert.True(ok);

        var reloaded = await Store.GetDecisionAsync(d.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("New title", reloaded!.Title);
        Assert.Equal("New body", reloaded.Text);
        Assert.Equal(DecisionStatus.Accepted, reloaded.Status);
        Assert.Equal(Confidence.High, reloaded.Confidence);
        Assert.Equal("Because of X", reloaded.Rationale);
        Assert.True(reloaded.UpdatedAtMs > reloaded.CreatedAtMs);
    }

    [Fact]
    public async Task UpdateDecision_returns_false_when_id_does_not_exist()
    {
        var missing = new Decision(Ids.NewUlid(), null, "t", "t");
        Assert.False(await Store.UpdateDecisionAsync(missing));
    }

    [Fact]
    public async Task DeleteDecision_removes_row_alternatives_and_links_via_triggers()
    {
        var d = new Decision(Ids.NewUlid(), null, "t", "body");
        var other = new Decision(Ids.NewUlid(), null, "other", "body");
        await Store.AddDecisionAsync(d,
            alternatives: [new Alternative(Ids.NewUlid(), d.Id, "Alt", "rejected")]);
        await Store.AddDecisionAsync(other);
        await Store.AddLinkAsync(new Link(Ids.NewUlid(),
            LinkEntity.Decision, d.Id, LinkEntity.Decision, other.Id, LinkRelation.RelatesTo));

        Assert.True(await Store.DeleteDecisionAsync(d.Id));

        Assert.Null(await Store.GetDecisionAsync(d.Id));
        Assert.Empty(await Store.GetAlternativesAsync(d.Id));
        Assert.Empty(await Store.GetLinksAsync(fromId: d.Id));
        Assert.NotNull(await Store.GetDecisionAsync(other.Id)); // unrelated row survives
    }

    [Fact]
    public async Task UpdatePrinciple_roundtrip_including_active_flag()
    {
        var p = new Principle(Ids.NewUlid(), null, "Old", "was");
        await Store.AddPrincipleAsync(p);

        var updated = p with { Title = "New", Statement = "is", Active = false };
        Assert.True(await Store.UpdatePrincipleAsync(updated));

        var reloaded = await Store.GetPrincipleAsync(p.Id);
        Assert.Equal("New", reloaded!.Title);
        Assert.Equal("is", reloaded.Statement);
        Assert.False(reloaded.Active);
    }

    [Fact]
    public async Task DeletePrinciple_removes_row()
    {
        var p = new Principle(Ids.NewUlid(), null, "t", "body");
        await Store.AddPrincipleAsync(p);

        Assert.True(await Store.DeletePrincipleAsync(p.Id));
        Assert.Null(await Store.GetPrincipleAsync(p.Id));
        Assert.False(await Store.DeletePrincipleAsync(p.Id)); // second call is a no-op
    }

    [Fact]
    public async Task UpdateNote_and_DeleteNote_roundtrip()
    {
        var n = new Note(Ids.NewUlid(), null, "Old", "was", Source: "a");
        await Store.AddNoteAsync(n);

        var updated = n with { Title = "New", Content = "is", Source = "b" };
        Assert.True(await Store.UpdateNoteAsync(updated));

        var reloaded = await Store.GetNoteAsync(n.Id);
        Assert.Equal("New", reloaded!.Title);
        Assert.Equal("is", reloaded.Content);
        Assert.Equal("b", reloaded.Source);

        Assert.True(await Store.DeleteNoteAsync(n.Id));
        Assert.Null(await Store.GetNoteAsync(n.Id));
    }

    [Fact]
    public async Task DeleteLink_removes_only_the_targeted_link()
    {
        var d1 = new Decision(Ids.NewUlid(), null, "d1", "body");
        var d2 = new Decision(Ids.NewUlid(), null, "d2", "body");
        await Store.AddDecisionAsync(d1);
        await Store.AddDecisionAsync(d2);

        var l1 = new Link(Ids.NewUlid(), LinkEntity.Decision, d1.Id, LinkEntity.Decision, d2.Id, LinkRelation.RelatesTo);
        var l2 = new Link(Ids.NewUlid(), LinkEntity.Decision, d2.Id, LinkEntity.Decision, d1.Id, LinkRelation.Supersedes);
        await Store.AddLinkAsync(l1);
        await Store.AddLinkAsync(l2);

        Assert.True(await Store.DeleteLinkAsync(l1.Id));

        var remaining = await Store.GetLinksAsync();
        Assert.Single(remaining);
        Assert.Equal(l2.Id, remaining[0].Id);
    }

    [Fact]
    public async Task Untag_removes_binding_but_keeps_tag()
    {
        var d = new Decision(Ids.NewUlid(), null, "d", "body");
        await Store.AddDecisionAsync(d);
        await Store.TagAsync(LinkEntity.Decision, d.Id, "resilience");
        Assert.Single(await Store.GetTagsAsync(LinkEntity.Decision, d.Id));

        Assert.True(await Store.UntagAsync(LinkEntity.Decision, d.Id, "resilience"));
        Assert.Empty(await Store.GetTagsAsync(LinkEntity.Decision, d.Id));

        // Tag is still in the catalog so re-tagging doesn't allocate a new id
        var tags = await Store.ListTagsAsync();
        Assert.Single(tags);
    }

    [Fact]
    public async Task DeleteTag_cascades_to_every_binding()
    {
        var d = new Decision(Ids.NewUlid(), null, "d", "body");
        var p = new Principle(Ids.NewUlid(), null, "p", "body");
        await Store.AddDecisionAsync(d);
        await Store.AddPrincipleAsync(p);
        await Store.TagAsync(LinkEntity.Decision, d.Id, "arch");
        await Store.TagAsync(LinkEntity.Principle, p.Id, "arch");

        Assert.True(await Store.DeleteTagAsync("arch"));

        Assert.Empty(await Store.GetTagsAsync(LinkEntity.Decision, d.Id));
        Assert.Empty(await Store.GetTagsAsync(LinkEntity.Principle, p.Id));
        Assert.Empty(await Store.ListTagsAsync());
    }
}
