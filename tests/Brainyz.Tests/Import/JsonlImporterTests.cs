// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Brainyz.Core;
using Brainyz.Core.Errors;
using Brainyz.Core.Export;
using Brainyz.Core.Import;
using Brainyz.Core.Storage;
using Brainyz.Tests.Export;

namespace Brainyz.Tests.Import;

public class JsonlImporterTests : StoreTestBase
{
    // ─────────── Manifest validation ───────────

    [Fact]
    public async Task Rejects_missing_file()
    {
        var importer = new JsonlImporter(Store, brainyzVersion: "0.3.0-test");
        var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
            importer.ImportAsync(
                path: Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.jsonl"),
                mode: ImportMode.Replace,
                skipSafetyBackup: true));
        Assert.Equal(ErrorCode.BZ_IMPORT_FILE_NOT_FOUND, ex.Code);
    }

    [Fact]
    public async Task Rejects_empty_file_as_manifest_missing()
    {
        var p = WriteLines("");
        try
        {
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                importer.ImportAsync(p, ImportMode.Replace, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_IMPORT_MANIFEST_MISSING, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_invalid_first_line_json()
    {
        var p = WriteLines("not json at all");
        try
        {
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                importer.ImportAsync(p, ImportMode.Replace, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_newer_format_version()
    {
        var p = WriteManifest(formatVersion: 2, schemaVersion: SchemaVersion.Current);
        try
        {
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                importer.ImportAsync(p, ImportMode.Replace, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_IMPORT_FORMAT_VERSION_NEWER, ex.Code);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public async Task Rejects_schema_mismatch()
    {
        var p = WriteManifest(formatVersion: 1, schemaVersion: 99);
        try
        {
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var ex = await Assert.ThrowsAsync<BrainyzException>(() =>
                importer.ImportAsync(p, ImportMode.Replace, skipSafetyBackup: true));
            Assert.Equal(ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH, ex.Code);
        }
        finally { File.Delete(p); }
    }

    // ─────────── Dry run ───────────

    [Fact]
    public async Task Dry_run_validates_without_mutating()
    {
        await ExportSeed.PopulateAsync(Store);
        var jsonl = await RunExportAsync(Store);
        try
        {
            var seedDecisionCount = (await Store.ListAllDecisionsAsync(limit: int.MaxValue)).Count;

            var importer = new JsonlImporter(Store, "0.3.0-test");
            var result = await importer.ImportAsync(jsonl, ImportMode.Replace,
                skipSafetyBackup: true, dryRun: true);

            // DB unchanged
            Assert.Equal(seedDecisionCount, (await Store.ListAllDecisionsAsync(limit: int.MaxValue)).Count);
            // Counts populated (dry-run parses every line and bumps `inserted`
            // as a side effect — we just confirm it parsed without throwing)
            Assert.True(result.InsertedCounts.ContainsKey("decision"));
        }
        finally { File.Delete(jsonl); }
    }

    // ─────────── Round-trip (replace) ───────────

    [Fact]
    public async Task Round_trip_replace_restores_every_entity_type()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);
        var jsonl = await RunExportAsync(Store);
        var destDb = Path.Combine(Path.GetTempPath(), $"rt-{Guid.NewGuid():N}.db");
        try
        {
            // Explicit dispose before the file cleanup in `finally` — otherwise
            // on Windows the DB handle remains open when we try to delete.
            var dest = await BrainStore.OpenAsync(destDb);
            try
            {
                var importer = new JsonlImporter(dest, "0.3.0-test");
                var result = await importer.ImportAsync(jsonl, ImportMode.Replace, skipSafetyBackup: true);

                Assert.False(result.RowCountMismatch);

                var origDecisions = await Store.ListAllDecisionsAsync(limit: int.MaxValue);
                var copyDecisions = await dest.ListAllDecisionsAsync(limit: int.MaxValue);
                Assert.Equal(origDecisions.Count, copyDecisions.Count);

                var orig = origDecisions.Single(d => d.Id == seeded.DecisionId);
                var copy = copyDecisions.Single(d => d.Id == seeded.DecisionId);
                Assert.Equal(orig.Title, copy.Title);
                Assert.Equal(orig.Rationale, copy.Rationale);

                var alt = await dest.GetAlternativesAsync(seeded.DecisionId);
                Assert.NotEmpty(alt);

                var links = await dest.GetLinksAsync(Brainyz.Core.Models.LinkEntity.Principle, seeded.PrincipleId);
                Assert.NotEmpty(links);
            }
            finally { await dest.DisposeAsync(); }
        }
        finally
        {
            File.Delete(jsonl);
            foreach (var f in new[] { destDb, destDb + "-wal", destDb + "-shm" })
                if (File.Exists(f)) try { File.Delete(f); } catch { /* Windows may still hold handles briefly */ }
        }
    }

    // ─────────── Additive mode ───────────

    [Fact]
    public async Task Additive_skips_existing_rows_and_adds_new_ones()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);
        var jsonl = await RunExportAsync(Store);
        try
        {
            // Re-import into the same DB additively → every ID collides.
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var result = await importer.ImportAsync(jsonl, ImportMode.Additive, skipSafetyBackup: true);

            // Every record in the export already existed → all skipped.
            Assert.Contains(result.SkippedCounts, kv => kv.Key == "decision" && kv.Value > 0);
            Assert.Contains(result.SkippedCounts, kv => kv.Key == "principle" && kv.Value > 0);

            // Counts in the DB unchanged (still 2 decisions from seed).
            var decisions = await Store.ListAllDecisionsAsync(limit: int.MaxValue);
            Assert.Equal(2, decisions.Count);
        }
        finally { File.Delete(jsonl); }
    }

    // ─────────── Safety backup ───────────

    [Fact]
    public async Task Replace_creates_pre_import_safety_backup()
    {
        await ExportSeed.PopulateAsync(Store);
        var jsonl = await RunExportAsync(Store);
        try
        {
            var importer = new JsonlImporter(Store, "0.3.0-test");
            var result = await importer.ImportAsync(jsonl, ImportMode.Replace, skipSafetyBackup: false);

            Assert.NotNull(result.SafetyBackupPath);
            Assert.True(File.Exists(result.SafetyBackupPath));
            Assert.Contains(".pre-import-", Path.GetFileName(result.SafetyBackupPath));

            if (File.Exists(result.SafetyBackupPath)) File.Delete(result.SafetyBackupPath);
        }
        finally { File.Delete(jsonl); }
    }

    // ─────────── Helpers ───────────

    private static async Task<string> RunExportAsync(BrainStore store, string? projectFilter = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rt-exp-{Guid.NewGuid():N}.jsonl");
        var exporter = new JsonlExporter(store, "0.3.0-test");
        await exporter.ExportAsync(path, projectFilter, includeHistory: false);
        return path;
    }

    private static string WriteLines(params string[] lines)
    {
        var p = Path.Combine(Path.GetTempPath(), $"import-fixture-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(p, lines);
        return p;
    }

    private static string WriteManifest(int formatVersion, int schemaVersion)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "manifest",
            format_version = formatVersion,
            brainyz_version = "0.3.0-fixture",
            schema_version = schemaVersion,
            exported_at_ms = 1_700_000_000_000L,
            project_filter = (string?)null,
            include_history = false,
            counts = new Dictionary<string, int>()
        });
        return WriteLines(json);
    }
}
