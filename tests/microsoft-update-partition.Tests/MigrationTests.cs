// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    /// <summary>
    /// Exercises the schema 10 -> 11 migration (PropertyIndexSchemaMigrator) against a real
    /// store built entirely by the pre-migration code (commit 39129c7), fetched via a real
    /// upstream-style AddPackages call and checked in as Fixtures/schema10-store.sqlite.
    /// Package identities below are fixed values baked into that fixture at generation time.
    /// </summary>
    public class MigrationTests
    {
        private static readonly Guid MainUpdateId = Guid.Parse("5c9b8794-c066-4570-95ee-60464a6fea57");
        private static readonly Guid SupersededUpdateId = Guid.Parse("6f588513-9e0e-434e-b138-48e9c957ccd6");
        private static readonly Guid BundledUpdateId = Guid.Parse("bd87c98e-7118-41af-b493-fdefb9843913");
        private static readonly Guid CategoryId = Guid.Parse("053ca307-9af9-4147-9504-7506c992e91a");
        private static readonly Guid DriverUpdateId = Guid.Parse("1cabf44d-2da3-4d7b-aa64-64414af76bdb");
        private const string ExpectedDigest = "Kql+1SziF0ezQuyGQRAFCw==";

        private static string CopyFixtureToTempStore()
        {
            var storePath = Path.Combine(Path.GetTempPath(), "upsync-migration-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storePath);
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema10-store.sqlite");
            File.Copy(fixturePath, Path.Combine(storePath, "metadata.sqlite"));
            return storePath;
        }

        [Fact]
        public void FixtureIsActuallyAtSchema10BeforeMigration()
        {
            var storePath = CopyFixtureToTempStore();
            try
            {
                using var connection = new SqliteConnection($"Data Source={Path.Combine(storePath, "metadata.sqlite")}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM store_properties WHERE key = 'schema_version';";
                var version = (string?)command.ExecuteScalar();
                Assert.Equal("10", version);

                using var tableCommand = connection.CreateCommand();
                tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'package_property_cache';";
                var tableExists = Convert.ToInt64(tableCommand.ExecuteScalar());
                Assert.Equal(0, tableExists);
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        [Fact]
        public void OpeningOldStoreMigratesSchemaAndBackfillsPropertyCache()
        {
            var storePath = CopyFixtureToTempStore();
            try
            {
                using (var store = SQLitePackageStore.OpenExisting(storePath))
                {
                    Assert.Equal(4, store.PackageCount);
                }

                using var connection = new SqliteConnection($"Data Source={Path.Combine(storePath, "metadata.sqlite")}");
                connection.Open();

                using var versionCommand = connection.CreateCommand();
                versionCommand.CommandText = "SELECT value FROM store_properties WHERE key = 'schema_version';";
                Assert.Equal("11", (string?)versionCommand.ExecuteScalar());

                using var builtCommand = connection.CreateCommand();
                builtCommand.CommandText = "SELECT value FROM store_properties WHERE key = 'property_index_built';";
                Assert.Equal("1", (string?)builtCommand.ExecuteScalar());

                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM package_property_cache;";
                var rowCount = Convert.ToInt64(countCommand.ExecuteScalar());
                Assert.True(rowCount > 0, "package_property_cache should have been backfilled with rows");
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        [Fact]
        public void BackfilledPropertiesMatchPreMigrationValues()
        {
            var storePath = CopyFixtureToTempStore();
            try
            {
                using var store = SQLitePackageStore.OpenExisting(storePath);

                var mainIdentity = new MicrosoftUpdatePackageIdentity(MainUpdateId, 1);
                var main = (SoftwareUpdate)store.GetPackage(mainIdentity);

                Assert.Equal("Fixture Main Update", main.Title);
                Assert.Equal("1000003", main.KBArticleId);
                Assert.Equal(CategoryId, Assert.Single(main.Categories));
                Assert.Equal(SupersededUpdateId, Assert.Single(main.SupersededUpdates));
                var bundled = Assert.Single(main.BundledUpdates);
                Assert.Equal(BundledUpdateId, bundled.ID);
                var file = Assert.Single(main.Files);
                Assert.Equal(ExpectedDigest, file.Digest.DigestBase64);

                var driver = (DriverUpdate)store.GetPackage(new MicrosoftUpdatePackageIdentity(DriverUpdateId, 1));
                Assert.Equal("Fixture Driver Update", driver.Title);
                var driverMetadata = Assert.Single(driver.GetDriverMetadata());
                Assert.Equal("555", driverMetadata.WhqlDriverID);
                Assert.Equal("FixtureCo", driverMetadata.Manufacturer);
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }
    }
}
