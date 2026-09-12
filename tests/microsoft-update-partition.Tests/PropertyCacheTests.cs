// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Prerequisites;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class PropertyCacheTests
    {
        private static SoftwareUpdate AddAndReadBack(SQLitePackageStore store, SoftwareUpdate package)
        {
            store.AddPackages(new IPackage[] { package });
            return (SoftwareUpdate)store.GetPackage(package.Id);
        }

        [Fact]
        public void TitleRoundTripsThroughReopen()
        {
            using var tempPath = new TempStorePath();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec { Title = "A Very Specific Title" };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                store.AddPackages(new IPackage[] { package });
                store.Flush();
            }

            using var reopened = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = reopened.GetPackage(package.Id);
            Assert.Equal("A Very Specific Title", readBack.Title);
        }

        [Fact]
        public void KBArticleIdRoundTrips()
        {
            using var tempPath = new TempStorePath();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec { KBArticleId = "9998887" };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            Assert.Equal("9998887", readBack.KBArticleId);
        }

        [Fact]
        public void CategoriesRoundTrip()
        {
            using var tempPath = new TempStorePath();
            var categoryA = Guid.NewGuid();
            var categoryB = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                AtLeastOnePrerequisites =
                {
                    (true, new List<Guid> { categoryA, categoryB })
                }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            Assert.Equal(new[] { categoryA, categoryB }.OrderBy(g => g), readBack.Categories.OrderBy(g => g));
        }

        [Fact]
        public void SimplePrerequisiteReconstructsCorrectly()
        {
            using var tempPath = new TempStorePath();
            var prereqId = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { prereqId }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            var simple = Assert.Single(readBack.Prerequisites);
            var typed = Assert.IsType<Simple>(simple);
            Assert.Equal(prereqId, typed.UpdateId);
        }

        [Fact]
        public void AtLeastOneNonCategoryPrerequisiteReconstructsCorrectly()
        {
            using var tempPath = new TempStorePath();
            var memberA = Guid.NewGuid();
            var memberB = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                AtLeastOnePrerequisites =
                {
                    (false, new List<Guid> { memberA, memberB })
                }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            var prereq = Assert.Single(readBack.Prerequisites);
            var atLeastOne = Assert.IsType<AtLeastOne>(prereq);
            Assert.False(atLeastOne.IsCategory);
            Assert.Equal(new[] { memberA, memberB }.OrderBy(g => g), atLeastOne.Simple.Select(s => s.UpdateId).OrderBy(g => g));
        }

        [Fact]
        public void AtLeastOneCategoryPrerequisiteReconstructsWithIsCategoryTrue()
        {
            using var tempPath = new TempStorePath();
            var categoryMember = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                AtLeastOnePrerequisites =
                {
                    (true, new List<Guid> { categoryMember })
                }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            var prereq = Assert.Single(readBack.Prerequisites);
            var atLeastOne = Assert.IsType<AtLeastOne>(prereq);
            Assert.True(atLeastOne.IsCategory);
            Assert.Equal(categoryMember, Assert.Single(atLeastOne.Simple).UpdateId);
        }

        [Fact]
        public void MixedPrerequisitesAllReconstructTogether()
        {
            using var tempPath = new TempStorePath();
            var simpleId = Guid.NewGuid();
            var groupA = Guid.NewGuid();
            var groupB = Guid.NewGuid();
            var categoryId = Guid.NewGuid();

            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { simpleId },
                AtLeastOnePrerequisites =
                {
                    (false, new List<Guid> { groupA, groupB }),
                    (true, new List<Guid> { categoryId })
                }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            Assert.Equal(3, readBack.Prerequisites.Count);
            Assert.Single(readBack.Prerequisites.OfType<Simple>());
            Assert.Equal(2, readBack.Prerequisites.OfType<AtLeastOne>().Count());
            Assert.Single(readBack.Prerequisites.OfType<AtLeastOne>(), a => a.IsCategory);
            Assert.Single(readBack.Prerequisites.OfType<AtLeastOne>(), a => !a.IsCategory);
        }

        [Fact]
        public void FilesRoundTrip()
        {
            using var tempPath = new TempStorePath();
            var digest = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                Files = { (digest, "payload.cab") }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            var file = Assert.Single(readBack.Files);
            Assert.Equal("payload.cab", file.FileName);
            Assert.Equal(digest, file.Digest.DigestBase64);
        }

        [Fact]
        public void SupersededUpdatesRoundTripOnSupersedingPackage()
        {
            using var tempPath = new TempStorePath();
            var supersededId = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                SupersededUpdates = { supersededId }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            Assert.Equal(supersededId, Assert.Single(readBack.SupersededUpdates));
        }

        [Fact]
        public void IsSupersededByFindsSupersedingPackage()
        {
            using var tempPath = new TempStorePath();
            var superseded = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var supersedingSpec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                SupersededUpdates = { superseded.Id.ID }
            };
            var superseding = SyntheticUpdates.BuildSoftwareUpdate(supersedingSpec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { superseded, superseding });

            var readBack = (SoftwareUpdate)store.GetPackage(superseded.Id);
            var supersededBy = Assert.Single(readBack.IsSupersededBy);
            Assert.Equal(superseding.Id, supersededBy);
        }

        [Fact]
        public void BundledUpdatesRoundTripOnBundlePackage()
        {
            using var tempPath = new TempStorePath();
            var bundledGuid = Guid.NewGuid();
            var spec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                BundledUpdates = { (bundledGuid, 1) }
            };
            var package = SyntheticUpdates.BuildSoftwareUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            var readBack = AddAndReadBack(store, package);

            var bundled = Assert.Single(readBack.BundledUpdates);
            Assert.Equal(bundledGuid, bundled.ID);
            Assert.Equal(1, bundled.Revision);
        }

        [Fact]
        public void BundledWithUpdatesFindsBundlePackage()
        {
            using var tempPath = new TempStorePath();
            var bundledMemberSpec = new SyntheticUpdates.SoftwareUpdateSpec { Revision = 1 };
            var bundledMember = SyntheticUpdates.BuildSoftwareUpdate(bundledMemberSpec);

            var bundleSpec = new SyntheticUpdates.SoftwareUpdateSpec
            {
                BundledUpdates = { (bundledMember.Id.ID, bundledMember.Id.Revision) }
            };
            var bundle = SyntheticUpdates.BuildSoftwareUpdate(bundleSpec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { bundledMember, bundle });

            var readBack = (SoftwareUpdate)store.GetPackage(bundledMember.Id);
            var bundledWith = Assert.Single(readBack.BundledWithUpdates);
            Assert.Equal(bundle.Id, bundledWith);
        }

        [Fact]
        public void DriverMetadataRoundTripsAllFieldsWithoutLoss()
        {
            using var tempPath = new TempStorePath();
            var distributionId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var spec = new SyntheticUpdates.DriverUpdateSpec
            {
                HardwareId = "pci\\ven_aaaa&dev_bbbb",
                WhqlDriverId = "424242",
                Manufacturer = "Fabrikam",
                Company = "Fabrikam Inc",
                Provider = "Fabrikam Provider",
                Class = "Display",
                DriverVerDate = "2023-06-01",
                DriverVerVersion = "1.2.3.4",
                FeatureScores = new() { ("10.0.0.0", 0xAB) },
                DistributionComputerHardwareIds = new() { distributionId },
                TargetComputerHardwareIds = new() { targetId }
            };
            var package = SyntheticUpdates.BuildDriverUpdate(spec);

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { package });
            store.Flush();

            var readBack = (DriverUpdate)store.GetPackage(package.Id);
            var metadata = Assert.Single(readBack.GetDriverMetadata());

            Assert.Equal("pci\\ven_aaaa&dev_bbbb", metadata.HardwareID);
            Assert.Equal("424242", metadata.WhqlDriverID);
            Assert.Equal("Fabrikam", metadata.Manufacturer);
            Assert.Equal("Fabrikam Inc", metadata.Company);
            Assert.Equal("Fabrikam Provider", metadata.Provider);
            Assert.Equal("Display", metadata.Class);
            var featureScore = Assert.Single(metadata.FeatureScores);
            Assert.Equal("10.0.0.0", featureScore.OperatingSystem);
            Assert.Equal(0xAB, featureScore.Score);
            Assert.Equal(distributionId, Assert.Single(metadata.DistributionComputerHardwareId));
            Assert.Equal(targetId, Assert.Single(metadata.TargetComputerHardwareId));
        }
    }
}
