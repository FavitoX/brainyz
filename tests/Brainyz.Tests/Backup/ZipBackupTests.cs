// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.Json;
using Brainyz.Core;
using Brainyz.Core.Backup;
using Brainyz.Core.Storage;
using Brainyz.Tests.Export;

namespace Brainyz.Tests.Backup;

public class ZipBackupTests : StoreTestBase
{
    [Fact]
    public async Task Zip_contains_manifest_db_and_readme()
    {
        await ExportSeed.PopulateAsync(Store);

        var outPath = Path.Combine(Path.GetTempPath(), $"bkp-{Guid.NewGuid():N}.zip");
        try
        {
            await new ZipBackup(Store, brainyzVersion: "0.3.0-test")
                .BackupAsync(outPath, CompressionLevel.Fastest);

            using var zip = ZipFile.OpenRead(outPath);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("brainyz.db"));
            Assert.NotNull(zip.GetEntry("README.txt"));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public async Task Manifest_json_has_required_fields()
    {
        await ExportSeed.PopulateAsync(Store);

        var outPath = Path.Combine(Path.GetTempPath(), $"bkp-{Guid.NewGuid():N}.zip");
        try
        {
            await new ZipBackup(Store, "0.3.0-test").BackupAsync(outPath, CompressionLevel.Fastest);

            using var zip = ZipFile.OpenRead(outPath);
            using var stream = zip.GetEntry("manifest.json")!.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            Assert.Equal(1, root.GetProperty("format_version").GetInt32());
            Assert.Equal("0.3.0-test", root.GetProperty("brainyz_version").GetString());
            Assert.Equal(SchemaVersion.Current, root.GetProperty("schema_version").GetInt32());
            Assert.True(root.GetProperty("created_at_ms").GetInt64() > 0);
            Assert.True(root.GetProperty("db_size_bytes").GetInt64() > 0);

            var counts = root.GetProperty("counts");
            Assert.True(counts.GetProperty("decisions").GetInt32() >= 2);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public async Task Embedded_db_opens_and_exposes_seeded_decision()
    {
        var seeded = await ExportSeed.PopulateAsync(Store);
        var outPath = Path.Combine(Path.GetTempPath(), $"bkp-{Guid.NewGuid():N}.zip");
        var extracted = Path.Combine(Path.GetTempPath(), $"bkp-extract-{Guid.NewGuid():N}.db");
        try
        {
            await new ZipBackup(Store, "0.3.0-test").BackupAsync(outPath, CompressionLevel.Fastest);

            using (var zip = ZipFile.OpenRead(outPath))
                zip.GetEntry("brainyz.db")!.ExtractToFile(extracted);

            var copy = await BrainStore.OpenAsync(extracted);
            try
            {
                var d = await copy.GetDecisionAsync(seeded.DecisionId);
                Assert.NotNull(d);
                Assert.Equal("LibSQL engine", d!.Title);
            }
            finally { await copy.DisposeAsync(); }
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            foreach (var f in new[] { extracted, extracted + "-wal", extracted + "-shm" })
                if (File.Exists(f)) try { File.Delete(f); } catch { /* best effort */ }
        }
    }
}
