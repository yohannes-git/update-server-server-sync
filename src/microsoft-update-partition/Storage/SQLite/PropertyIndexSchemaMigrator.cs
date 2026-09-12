// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using System;

namespace Microsoft.PackageGraph.Storage.Local
{
    /// <summary>
    /// Performs the one-time schema 10 to 11 migration: creates the
    /// package_property_cache/package_custom_key_index tables that replace the
    /// in-memory ZipStreamIndexContainer engine. The tables are left empty;
    /// SQLitePackageStore detects the missing property_index_built marker on
    /// open and backfills them in batches via CheckIndex(true), the same path
    /// used to build them for newly added packages.
    /// </summary>
    internal static class PropertyIndexSchemaMigrator
    {
        private const int SourceSchemaVersion = 10;
        private const int TargetSchemaVersion = 11;

        public static bool TryMigrate(SqliteConnection connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (!TableExists(connection, "store_properties") || !TableExists(connection, "packages"))
            {
                return false;
            }

            var currentVersion = ReadSchemaVersion(connection);
            if (currentVersion != SourceSchemaVersion)
            {
                return false;
            }

            using var transaction = connection.BeginTransaction(deferred: false);

            currentVersion = ReadSchemaVersion(connection, transaction);
            if (currentVersion != SourceSchemaVersion)
            {
                transaction.Rollback();
                return false;
            }

            using (var createCommand = connection.CreateCommand())
            {
                createCommand.Transaction = transaction;
                createCommand.CommandText = @"
CREATE TABLE IF NOT EXISTS package_property_cache (
    package_index INTEGER NOT NULL,
    property_name TEXT NOT NULL,
    value_json TEXT NOT NULL,
    PRIMARY KEY(package_index, property_name),
    FOREIGN KEY(package_index) REFERENCES packages(package_index) ON DELETE CASCADE
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS package_custom_key_index (
    index_name TEXT NOT NULL,
    key_text TEXT NOT NULL,
    package_index INTEGER NOT NULL,
    PRIMARY KEY(index_name, key_text, package_index),
    FOREIGN KEY(package_index) REFERENCES packages(package_index) ON DELETE CASCADE
) WITHOUT ROWID;

CREATE INDEX IF NOT EXISTS idx_package_custom_key_index_lookup
ON package_custom_key_index(index_name, key_text);";
                createCommand.ExecuteNonQuery();
            }

            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = @"
UPDATE store_properties
SET value = $targetVersion
WHERE key = 'schema_version';";
                versionCommand.Parameters.AddWithValue(
                    "$targetVersion",
                    TargetSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                versionCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(*)
FROM sqlite_schema
WHERE type = 'table'
  AND name = $tableName;";
            command.Parameters.AddWithValue("$tableName", tableName);
            return Convert.ToInt32(
                command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT value
FROM store_properties
WHERE key = 'schema_version'
LIMIT 1;";
            var value = command.ExecuteScalar();
            return value != null
                && value != DBNull.Value
                && int.TryParse((string)value, out var version)
                    ? version
                    : -1;
        }
    }
}
