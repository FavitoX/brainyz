// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core;
using Brainyz.Core.Models;

namespace Brainyz.Tests;

public sealed class NotesTests : StoreTestBase
{
    [Fact]
    public async Task AddNote_roundtrip()
    {
        var n = new Note(
            Id: Ids.NewUlid(),
            ProjectId: null,
            Title: "Stripe v2024-01 changed refunds",
            Content: "Refunds now return a 202 and require polling for final status.",
            Source: "https://stripe.com/changelog/2024-01");

        await Store.AddNoteAsync(n);
        var found = await Store.GetNoteAsync(n.Id);

        Assert.NotNull(found);
        Assert.Equal("Stripe v2024-01 changed refunds", found!.Title);
        Assert.Equal(n.Content, found.Content);
        Assert.Equal(n.Source, found.Source);
        Assert.True(found.Active);
    }

    [Fact]
    public async Task SearchNotes_matches_via_FTS5_on_content()
    {
        await Store.AddNoteAsync(new Note(Ids.NewUlid(), null,
            "Stripe refunds behavior", "Refunds now return a 202 status"));
        await Store.AddNoteAsync(new Note(Ids.NewUlid(), null,
            "Linear team", "The payments team tracks work in Linear"));

        var hits = await Store.SearchNotesAsync("Linear");
        Assert.Single(hits);
        Assert.Equal("Linear team", hits[0].Title);
    }
}
