// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Storage;

namespace Brainyz.Tests.Export;

public class JsonlExporterTests : StoreTestBase
{
    private async Task<(string path, List<JsonElement> records)> ExportAsync(
        string? projectFilter = null, bool includeHistory = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"brainyz-exp-{Guid.NewGuid():N}.jsonl");
        var exporter = new JsonlExporter(Store, brainyzVersion: "0.3.0-test");
        await exporter.ExportAsync(path, projectFilter, includeHistory);
        var records = File.ReadLines(path)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();
        return (path, records);
    }

    // ─────────── Manifest ───────────

    [Fact]
    public async Task Writes_manifest_as_first_line()
    {
        await ExportSeed.PopulateAsync(Store);
        var (path, records) = await ExportAsync();
        try
        {
            var first = records[0];
            Assert.Equal("manifest", first.GetProperty("type").GetString());
            Assert.Equal(1, first.GetProperty("format_version").GetInt32());
            Assert.Equal("0.3.0-test", first.GetProperty("brainyz_version").GetString());
            Assert.Equal(SchemaVersion.Current, first.GetProperty("schema_version").GetInt32());
            Assert.False(first.GetProperty("include_history").GetBoolean());
            Assert.Equal(JsonValueKind.Null, first.GetProperty("project_filter").ValueKind);
            Assert.True(first.GetProperty("exported_at_ms").GetInt64() > 0);

            var counts = first.GetProperty("counts");
            Assert.True(counts.GetProperty("project").GetInt32() >= 2);
            Assert.True(counts.GetProperty("decision").GetInt32() >= 2);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ─────────── Entity records ───────────

    [Fact]
    public async Task Emits_every_entity_type_in_topological_order()
    {
        await ExportSeed.PopulateAsync(Store);
        var (path, records) = await ExportAsync();
        try
        {
            var types = records.Select(r => r.GetProperty("type").GetString()!).ToList();

            Assert.Equal("manifest", types[0]);
            // Topological order per §3.2
            Assert.Contains("project", types);
            Assert.Contains("project_remote", types);
            Assert.Contains("decision", types);
            Assert.Contains("alternative", types);
            Assert.Contains("principle", types);
            Assert.Contains("note", types);
            Assert.Contains("tag", types);
            Assert.Contains("tag_link", types);
            Assert.Contains("link", types);
            Assert.Contains("embedding", types);

            // Alternatives must come after their decisions
            var decIdx = types.IndexOf("decision");
            var altIdx = types.IndexOf("alternative");
            Assert.True(altIdx > decIdx, "alternative records must come after decision records");

            // Embeddings last (among data types)
            var embIdx = types.LastIndexOf("embedding");
            var linkIdx = types.LastIndexOf("link");
            Assert.True(embIdx > linkIdx, "embedding records must come after link records");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ─────────── Tags / links ───────────

    [Fact]
    public async Task Emits_tag_tag_link_and_polymorphic_link_records()
    {
        await ExportSeed.PopulateAsync(Store);
        var (path, records) = await ExportAsync();
        try
        {
            var tag = records.Single(r => r.GetProperty("type").GetString() == "tag");
            Assert.Equal("resilience", tag.GetProperty("path").GetString());

            var tagLink = records.Single(r => r.GetProperty("type").GetString() == "tag_link");
            Assert.Equal("decision", tagLink.GetProperty("entity_type").GetString());

            var link = records.Single(r => r.GetProperty("type").GetString() == "link");
            Assert.Equal("derived_from", link.GetProperty("relation_type").GetString());
            Assert.Equal("principle condensed the decision", link.GetProperty("note").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ─────────── Embeddings (base64) ───────────

    [Fact]
    public async Task Embeddings_are_base64_encoded_in_little_endian_binary32()
    {
        await ExportSeed.PopulateAsync(Store);
        var (path, records) = await ExportAsync();
        try
        {
            var emb = records.Single(r => r.GetProperty("type").GetString() == "embedding");
            Assert.Equal(768, emb.GetProperty("dim").GetInt32());
            Assert.Equal("nomic-embed-text:v1.5", emb.GetProperty("model").GetString());

            var base64 = emb.GetProperty("vector_base64").GetString()!;
            var bytes = Convert.FromBase64String(base64);
            Assert.Equal(768 * 4, bytes.Length);

            // The first float was sin(0) = 0. Check the first four bytes.
            Assert.Equal(0.0f, BitConverter.ToSingle(bytes, 0));

            // The second float was sin(0.01) ≈ 0.0099998333...
            var secondFloat = BitConverter.ToSingle(bytes, 4);
            Assert.InRange(secondFloat, 0.0099f, 0.01f);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ─────────── History opt-in ───────────

    [Fact]
    public async Task Include_history_flag_controls_history_entry_emission()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);

        // Trigger history by updating the decision once.
        var d = (await Store.GetDecisionAsync(seeded.DecisionId))!;
        await Store.UpdateDecisionAsync(d with { Title = "LibSQL engine — confirmed" });

        var (pathOff, recOff) = await ExportAsync(includeHistory: false);
        var (pathOn, recOn)   = await ExportAsync(includeHistory: true);
        try
        {
            Assert.DoesNotContain(recOff, r => r.GetProperty("type").GetString() == "history_entry");
            Assert.Contains(recOn,        r => r.GetProperty("type").GetString() == "history_entry");
        }
        finally
        {
            if (File.Exists(pathOff)) File.Delete(pathOff);
            if (File.Exists(pathOn)) File.Delete(pathOn);
        }
    }

    // ─────────── --project filter ───────────

    [Fact]
    public async Task Project_filter_excludes_data_from_other_projects()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);
        var (path, records) = await ExportAsync(projectFilter: seeded.ProjectId);
        try
        {
            var decisionIds = records
                .Where(r => r.GetProperty("type").GetString() == "decision")
                .Select(r => r.GetProperty("id").GetString())
                .ToList();

            Assert.Contains(seeded.DecisionId, decisionIds);
            Assert.DoesNotContain(seeded.OtherDecisionId, decisionIds);

            // Manifest records the filter.
            var manifest = records[0];
            Assert.Equal(seeded.ProjectId, manifest.GetProperty("project_filter").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Nonexistent_project_filter_is_rejected()
    {
        await ExportSeed.PopulateAsync(Store);
        var outPath = Path.Combine(Path.GetTempPath(), $"brainyz-exp-{Guid.NewGuid():N}.jsonl");
        var exporter = new JsonlExporter(Store, "0.3.0-test");

        var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
            exporter.ExportAsync(outPath, projectFilter: "01JKX-bogus-id", includeHistory: false));

        Assert.Equal(ErrorCode.BZ_EXPORT_PROJECT_NOT_FOUND, ex.Code);
        Assert.False(File.Exists(outPath), "no file should be written when filter is invalid");
    }
}
