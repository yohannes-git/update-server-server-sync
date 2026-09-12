// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class SupersededPruneTests
    {
        private static void AddAndPublish(string path, params IPackage[] packages)
        {
            using var store = SQLitePackageStore.OpenOrCreate(path);
            store.AddPackages(packages);
            store.Flush();
        }

        private static Guid Id(SoftwareUpdate update) => ((MicrosoftUpdatePackageIdentity)update.Id).ID;

        [Fact]
        public void SupersededLeafIsPrunedAfterSafetyMargin()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var oldLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var newLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(oldLeaf) }
            });
            AddAndPublish(tempPath.Path, oldLeaf, newLeaf);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var pruneStore = (ISupersededUpdatePruneStore)store;

            var status = pruneStore.GetSupersededPruneStatus(0);
            Assert.Equal(1, status.PrunableCount);

            var deleted = pruneStore.PruneSupersededUpdates(0);
            Assert.Equal(1, deleted);
            Assert.False(store.ContainsPackage(oldLeaf.Id));
            Assert.True(store.ContainsPackage(newLeaf.Id));
            Assert.Equal(1, store.PackageCount);
        }

        [Fact]
        public void SupersededLeafNotPrunedBeforeSafetyMargin()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var oldLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var newLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(oldLeaf) }
            });
            AddAndPublish(tempPath.Path, oldLeaf, newLeaf);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var pruneStore = (ISupersededUpdatePruneStore)store;

            var status = pruneStore.GetSupersededPruneStatus(30);
            Assert.Equal(0, status.PrunableCount);

            var deleted = pruneStore.PruneSupersededUpdates(30);
            Assert.Equal(0, deleted);
            Assert.True(store.ContainsPackage(oldLeaf.Id));
            Assert.Equal(2, store.PackageCount);
        }

        [Fact]
        public void BundleMemberProtectedFromPruningDespiteBeingSuperseded()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var member = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var supersedingLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(member) }
            });
            var bundle = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                BundledUpdates = { (Id(member), member.Id.Revision) }
            });
            AddAndPublish(tempPath.Path, member, supersedingLeaf, bundle);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var pruneStore = (ISupersededUpdatePruneStore)store;

            var status = pruneStore.GetSupersededPruneStatus(0);
            Assert.Equal(0, status.PrunableCount);

            var deleted = pruneStore.PruneSupersededUpdates(0);
            Assert.Equal(0, deleted);
            Assert.True(store.ContainsPackage(member.Id));
            Assert.Equal(3, store.PackageCount);
        }

        [Fact]
        public void SupersedesEdgeWithNoStoredTargetYieldsNoCandidates()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var neverStoredUpdateId = Guid.NewGuid();
            var supersedingLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { neverStoredUpdateId }
            });
            AddAndPublish(tempPath.Path, supersedingLeaf);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var pruneStore = (ISupersededUpdatePruneStore)store;

            var status = pruneStore.GetSupersededPruneStatus(0);
            Assert.Equal(0, status.PrunableCount);

            var deleted = pruneStore.PruneSupersededUpdates(0);
            Assert.Equal(0, deleted);
            Assert.Equal(1, store.PackageCount);
        }

        [Fact]
        public void PruneCascadesCleanupAndPreservesKeptPackageServing()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var oldLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var newLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(oldLeaf) }
            });
            AddAndPublish(tempPath.Path, oldLeaf, newLeaf);

            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                ((ISupersededUpdatePruneStore)store).PruneSupersededUpdates(0);
            }

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var results = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);

            var record = Assert.Single(results);
            Assert.Equal(newLeaf.Id, record.Package.Id);

            using var reopened = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            Assert.False(reopened.ContainsPackage(oldLeaf.Id));
        }

        [Fact]
        public void PruningDoesNotStrandASharedFileStillNeededByAKeptPackage()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var sharedDigestBytes = Guid.NewGuid().ToByteArray();
            var sharedDigest = Convert.ToBase64String(sharedDigestBytes);

            var oldLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                Files = { (sharedDigest, "shared.cab") }
            });
            var newLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(oldLeaf) },
                Files = { (sharedDigest, "shared.cab") }
            });
            AddAndPublish(tempPath.Path, oldLeaf, newLeaf);

            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                var deleted = ((ISupersededUpdatePruneStore)store).PruneSupersededUpdates(0);
                Assert.Equal(1, deleted);
            }

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var located = clientSync.GetFileLocations(new[] { sharedDigestBytes });
            var record = Assert.Single(located);
            Assert.Equal("http://mu/shared.cab", record.Url);
        }

        [Fact]
        public void DryRunStatusDoesNotModifyStore()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var oldLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var newLeaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                SupersededUpdates = { Id(oldLeaf) }
            });
            AddAndPublish(tempPath.Path, oldLeaf, newLeaf);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var pruneStore = (ISupersededUpdatePruneStore)store;

            pruneStore.GetSupersededPruneStatus(0);
            pruneStore.GetSupersededPruneStatus(0);

            Assert.Equal(2, store.PackageCount);
            Assert.True(store.ContainsPackage(oldLeaf.Id));
        }
    }
}
