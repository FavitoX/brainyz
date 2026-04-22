// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class LinksTests : StoreTestBase
{
    [Fact]
    public async Task AddLink_persists_a_cross_entity_link()
    {
        var d = new Decision(Ids.NewUlid(), null, "Use LibSQL", "Embedded LibSQL with vectors");
        var p = new Principle(Ids.NewUlid(), null, "Op simplicity", "One file, one process");
        await Store.AddDecisionAsync(d);
        await Store.AddPrincipleAsync(p);

        var link = new Link(
            Id: Ids.NewUlid(),
            FromType: LinkEntity.Principle,
            FromId: p.Id,
            ToType: LinkEntity.Decision,
            ToId: d.Id,
            Relation: LinkRelation.DerivedFrom,
            Annotation: "Principle emerged after the LibSQL decision");

        await Store.AddLinkAsync(link);

        var outgoing = await Store.GetLinksAsync(fromType: LinkEntity.Principle, fromId: p.Id);
        Assert.Single(outgoing);
        var got = outgoing[0];
        Assert.Equal(LinkEntity.Decision, got.ToType);
        Assert.Equal(d.Id, got.ToId);
        Assert.Equal(LinkRelation.DerivedFrom, got.Relation);
        Assert.Equal("Principle emerged after the LibSQL decision", got.Annotation);
    }

    [Fact]
    public async Task GetLinks_filters_by_relation()
    {
        var d1 = new Decision(Ids.NewUlid(), null, "D1", "t1");
        var d2 = new Decision(Ids.NewUlid(), null, "D2", "t2");
        var d3 = new Decision(Ids.NewUlid(), null, "D3", "t3");
        await Store.AddDecisionAsync(d1);
        await Store.AddDecisionAsync(d2);
        await Store.AddDecisionAsync(d3);

        await Store.AddLinkAsync(new Link(Ids.NewUlid(),
            LinkEntity.Decision, d2.Id, LinkEntity.Decision, d1.Id, LinkRelation.Supersedes));
        await Store.AddLinkAsync(new Link(Ids.NewUlid(),
            LinkEntity.Decision, d3.Id, LinkEntity.Decision, d1.Id, LinkRelation.RelatesTo));

        var supersedes = await Store.GetLinksAsync(toId: d1.Id, relation: LinkRelation.Supersedes);
        Assert.Single(supersedes);
        Assert.Equal(d2.Id, supersedes[0].FromId);

        var relates = await Store.GetLinksAsync(toId: d1.Id, relation: LinkRelation.RelatesTo);
        Assert.Single(relates);
        Assert.Equal(d3.Id, relates[0].FromId);
    }

    [Fact]
    public async Task GetLinks_with_no_filters_returns_all()
    {
        var d = new Decision(Ids.NewUlid(), null, "D", "t");
        var n = new Note(Ids.NewUlid(), null, "N", "content");
        await Store.AddDecisionAsync(d);
        await Store.AddNoteAsync(n);

        await Store.AddLinkAsync(new Link(Ids.NewUlid(),
            LinkEntity.Note, n.Id, LinkEntity.Decision, d.Id, LinkRelation.InformedBy));

        var all = await Store.GetLinksAsync();
        Assert.Single(all);
    }
}
