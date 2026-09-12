// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.PackageGraph.Storage;
using System;

namespace Microsoft.PackageGraph.Utilitites.Upsync
{
    internal static class SupersededPruneMaintenanceCommand
    {
        public static void Prune(PruneSupersededOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.SupersededForDays < 0)
            {
                ConsoleOutput.WriteRed("--superseded-for-days must be zero or greater.");
                return;
            }

            using var store = MetadataStoreCreator.OpenFromOptions(options);
            if (store == null)
            {
                return;
            }

            if (store is not ISupersededUpdatePruneStore pruneStore)
            {
                ConsoleOutput.WriteRed(
                    "This metadata store does not support pruning superseded updates. " +
                    "Use a local SQLite store.");
                return;
            }

            var status = pruneStore.GetSupersededPruneStatus(options.SupersededForDays);

            Console.WriteLine("Superseded update maintenance");
            Console.WriteLine("==============================");
            Console.WriteLine($"Superseded for at least   : {status.SupersededForDays} day(s)");
            Console.WriteLine($"Prunable update(s)        : {status.PrunableCount}");
            Console.WriteLine($"Estimated metadata bytes  : {status.EstimatedMetadataBytes}");

            if (options.DryRun)
            {
                Console.WriteLine();
                Console.WriteLine("Dry run: SQLite was not modified.");
                return;
            }

            var deleted = pruneStore.PruneSupersededUpdates(options.SupersededForDays);

            Console.WriteLine();
            ConsoleOutput.WriteGreen($"Deleted {deleted} superseded update(s).");
        }
    }
}
