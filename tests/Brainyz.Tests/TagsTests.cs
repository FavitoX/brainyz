// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class TagsTests : StoreTestBase
{
    [Fact]
    public async Task EnsureTag_creates_new_tag_and_returns_id()
    {
        var id = await Store.EnsureTagAsync("payments/idempotency");

        Assert.Equal(26, id.Length);

        var all = await Store.ListTagsAsync();
        Assert.Single(all);
        Assert.Equal("payments/idempotency", all[0].Path);
    }

    [Fact]
    public async Task EnsureTag_is_idempotent_for_same_path()
    {
        var id1 = await Store.EnsureTagAsync("ailang/auth");
        var id2 = await Store.EnsureTagAsync("ailang/auth");

        Assert.Equal(id1, id2);
        var all = await Store.ListTagsAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task TagAsync_attaches_tag_to_a_decision()
    {
        var d = new Decision(Ids.NewUlid(), null, "D", "body");
        await Store.AddDecisionAsync(d);

        await Store.TagAsync(LinkEntity.Decision, d.Id, "resilience");
        await Store.TagAsync(LinkEntity.Decision, d.Id, "storage");

        var tags = await Store.GetTagsAsync(LinkEntity.Decision, d.Id);

        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, t => t.Path == "resilience");
        Assert.Contains(tags, t => t.Path == "storage");
    }

    [Fact]
    public async Task TagAsync_is_idempotent_for_same_entity_plus_tag()
    {
        var d = new Decision(Ids.NewUlid(), null, "D", "body");
        await Store.AddDecisionAsync(d);

        await Store.TagAsync(LinkEntity.Decision, d.Id, "resilience");
        await Store.TagAsync(LinkEntity.Decision, d.Id, "resilience");

        var tags = await Store.GetTagsAsync(LinkEntity.Decision, d.Id);
        Assert.Single(tags);
    }

    [Fact]
    public async Task Tags_work_for_principles_and_notes_too()
    {
        var p = new Principle(Ids.NewUlid(), null, "Simple", "One file");
        var n = new Note(Ids.NewUlid(), null, "Stripe", "content");
        await Store.AddPrincipleAsync(p);
        await Store.AddNoteAsync(n);

        await Store.TagAsync(LinkEntity.Principle, p.Id, "arch");
        await Store.TagAsync(LinkEntity.Note, n.Id, "vendor/stripe");

        Assert.Single(await Store.GetTagsAsync(LinkEntity.Principle, p.Id));
        Assert.Single(await Store.GetTagsAsync(LinkEntity.Note, n.Id));
    }
}
