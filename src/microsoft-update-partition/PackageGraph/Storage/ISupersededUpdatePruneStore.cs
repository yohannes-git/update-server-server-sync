// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.PackageGraph.Storage
{
    /// <summary>Reports how many superseded updates could be reclaimed, and how much space they occupy.</summary>
    public sealed class SupersededPruneStatus
    {
        /// <summary>Gets the active lower bound: only updates superseded before this many days ago are counted.</summary>
        public int SupersededForDays { get; }

        /// <summary>Gets the number of packages eligible for pruning.</summary>
        public long PrunableCount { get; }

        /// <summary>Gets the approximate number of bytes occupied by the eligible packages' stored metadata.</summary>
        public long EstimatedMetadataBytes { get; }

        /// <summary>Creates a superseded-prune status.</summary>
        public SupersededPruneStatus(int supersededForDays, long prunableCount, long estimatedMetadataBytes)
        {
            SupersededForDays = supersededForDays;
            PrunableCount = prunableCount;
            EstimatedMetadataBytes = estimatedMetadataBytes;
        }
    }

    /// <summary>
    /// Optional capability implemented by stores that can reclaim disk space occupied by
    /// software updates that have been superseded for a while and are no longer bundled
    /// by anything still published.
    /// </summary>
    public interface ISupersededUpdatePruneStore
    {
        /// <summary>Reports how many packages are eligible for pruning, without deleting anything.</summary>
        SupersededPruneStatus GetSupersededPruneStatus(int supersededForDays);

        /// <summary>Deletes packages superseded for at least the given number of days. Returns the number deleted.</summary>
        long PruneSupersededUpdates(int supersededForDays);
    }
}
