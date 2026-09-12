// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class ClientSyncTests
    {
        private static void AddAndPublish(string path, params IPackage[] packages)
        {
            using var store = SQLitePackageStore.OpenOrCreate(path);
            store.AddPackages(packages);
            store.Flush();
        }

        private static Guid Id(SoftwareUpdate update) => ((MicrosoftUpdatePackageIdentity)update.Id).ID;

        [Fact]
        public void LeafCandidateReturnedWhenPrerequisiteSatisfied()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var results = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out var truncated);

            var record = Assert.Single(results);
            Assert.Equal(leaf.Id, record.Package.Id);
            Assert.False(record.IsBundle);
            Assert.False(record.IsBundled);
            Assert.False(truncated);
        }

        [Fact]
        public void LeafCandidateNotReturnedWhenPrerequisiteUnsatisfied()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var results = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);

            Assert.Empty(results);
        }

        [Fact]
        public void RootStageReturnsPrerequisiteWithNoOwnPrerequisitesButWithDependents()
        {
            using var tempPath = new TempStorePath();
            var detectoid = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { Id(detectoid) }
            });
            AddAndPublish(tempPath.Path, detectoid, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var results = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Root,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);

            var record = Assert.Single(results);
            Assert.Equal(detectoid.Id, record.Package.Id);
        }

        [Fact]
        public void CandidateExcludedWhenInInstalledOrCachedSets()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var excludedByCache = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                new[] { Id(leaf) },
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);

            Assert.Empty(excludedByCache);
        }

        [Fact]
        public void ApprovalGatesUnapprovedSoftwareUpdatesUnlessApproveAll()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            var notApproved = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: false,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);
            Assert.Empty(notApproved);

            var explicitlyApproved = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: false,
                new[] { (MicrosoftUpdatePackageIdentity)leaf.Id },
                maxResults: 10,
                out _);
            Assert.Single(explicitlyApproved);

            var approveAll = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);
            Assert.Single(approveAll);
        }

        [Fact]
        public void BundleAndBundledLeafAppearAtTheirRespectiveStages()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var bundleMember = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            var bundle = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId },
                BundledUpdates = { (Id(bundleMember), bundleMember.Id.Revision) }
            });
            AddAndPublish(tempPath.Path, bundleMember, bundle);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            var bundledLeaf = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.BundledLeaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);
            var bundledRecord = Assert.Single(bundledLeaf);
            Assert.Equal(bundleMember.Id, bundledRecord.Package.Id);
            Assert.True(bundledRecord.IsBundled);

            var leafStage = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out _);
            var bundleRecord = Assert.Single(leafStage);
            Assert.Equal(bundle.Id, bundleRecord.Package.Id);
            Assert.True(bundleRecord.IsBundle);
            Assert.False(bundleRecord.IsBundled);
        }

        [Fact]
        public void SupersededLeafExcludedFromLeafStageButSupersedingLeafIsNot()
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
        }

        [Fact]
        public void MaxResultsTruncatesAndReportsTruncated()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaves = Enumerable.Range(0, 3)
                .Select(_ => SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
                {
                    SimplePrerequisites = { detectoidId }
                }))
                .ToArray();
            AddAndPublish(tempPath.Path, leaves);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            var limited = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 2,
                out var truncatedWhenLimited);
            Assert.Equal(2, limited.Count);
            Assert.True(truncatedWhenLimited);

            var unlimited = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                new[] { detectoidId },
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 5,
                out var truncatedWhenNotLimited);
            Assert.Equal(3, unlimited.Count);
            Assert.False(truncatedWhenNotLimited);
        }

        [Fact]
        public void HasLaterSoftwareCandidatesReflectsRemainingGraph()
        {
            using var tempPath = new TempStorePath();
            var detectoidId = Guid.NewGuid();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, leaf);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            Assert.True(clientSync.HasLaterSoftwareCandidates(
                ClientSyncSoftwareStage.Root,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>()));

            Assert.False(clientSync.HasLaterSoftwareCandidates(
                ClientSyncSoftwareStage.Leaf,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>()));
        }

        [Fact]
        public void HasLaterSoftwareCandidatesIsFalseOnEmptyStore()
        {
            using var tempPath = new TempStorePath();
            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                store.Flush();
            }

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            Assert.False(clientSync.HasLaterSoftwareCandidates(
                ClientSyncSoftwareStage.Root,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>()));
        }

        [Fact]
        public void GetSoftwareCandidatesOnEmptyStoreReturnsEmptyWithoutThrowing()
        {
            using var tempPath = new TempStorePath();
            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                store.Flush();
            }

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var results = clientSync.GetSoftwareCandidates(
                ClientSyncSoftwareStage.Root,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                approveAllSoftwareUpdates: true,
                Array.Empty<MicrosoftUpdatePackageIdentity>(),
                maxResults: 10,
                out var truncated);

            Assert.Empty(results);
            Assert.False(truncated);
        }

        [Fact]
        public void GetFileLocationsResolvesKnownDigestAndIgnoresUnknownOnes()
        {
            using var tempPath = new TempStorePath();
            var digestBytes = Guid.NewGuid().ToByteArray();
            var digest = Convert.ToBase64String(digestBytes);
            var package = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                Files = { (digest, "payload.cab") }
            });
            AddAndPublish(tempPath.Path, package);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);
            var unknownDigest = Guid.NewGuid().ToByteArray();
            var located = clientSync.GetFileLocations(new[] { digestBytes, unknownDigest });

            var record = Assert.Single(located);
            Assert.Equal(digestBytes, record.Digest);
            Assert.Equal("http://mu/payload.cab", record.Url);
        }

        [Fact]
        public void MatchDriverFindsUnrestrictedDriverByHardwareId()
        {
            using var tempPath = new TempStorePath();
            var spec = new SyntheticUpdates.DriverUpdateSpec
            {
                HardwareId = "pci\\ven_aaaa&dev_bbbb",
                DistributionComputerHardwareIds = new(),
                TargetComputerHardwareIds = new()
            };
            var driver = SyntheticUpdates.BuildDriverUpdate(spec);
            AddAndPublish(tempPath.Path, driver);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            var matched = clientSync.MatchDriver(
                new[] { spec.HardwareId },
                Array.Empty<Guid>(),
                Array.Empty<Guid>());
            Assert.NotNull(matched);
            Assert.Equal(spec.HardwareId.ToLowerInvariant(), matched.MatchedHardwareId);
            Assert.Equal(driver.Id, matched.Driver.Id);

            var notMatched = clientSync.MatchDriver(
                new[] { "pci\\ven_ffff&dev_ffff" },
                Array.Empty<Guid>(),
                Array.Empty<Guid>());
            Assert.Null(notMatched);
        }

        [Fact]
        public void MatchDriverRespectsComputerHardwareIdRestriction()
        {
            using var tempPath = new TempStorePath();
            var restrictedComputerId = Guid.NewGuid();
            var spec = new SyntheticUpdates.DriverUpdateSpec
            {
                HardwareId = "pci\\ven_cccc&dev_dddd",
                DistributionComputerHardwareIds = new() { restrictedComputerId },
                TargetComputerHardwareIds = new()
            };
            var driver = SyntheticUpdates.BuildDriverUpdate(spec);
            AddAndPublish(tempPath.Path, driver);

            using var clientSync = new SQLiteClientSyncMetadataStore(tempPath.Path);

            var withoutMatchingComputer = clientSync.MatchDriver(
                new[] { spec.HardwareId },
                Array.Empty<Guid>(),
                Array.Empty<Guid>());
            Assert.Null(withoutMatchingComputer);

            var withMatchingComputer = clientSync.MatchDriver(
                new[] { spec.HardwareId },
                new[] { restrictedComputerId },
                Array.Empty<Guid>());
            Assert.NotNull(withMatchingComputer);
            Assert.Equal(restrictedComputerId, withMatchingComputer.MatchedComputerHardwareId);
        }
    }
}
