// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class IdentityResolutionTests
    {
        [Fact]
        public void AddedPackageIsFoundByIdentityAndIndex()
        {
            using var tempPath = new TempStorePath();
            var package = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                store.AddPackages(new[] { package });

                Assert.True(store.ContainsPackage(package.Id));
                var packageIndex = store.GetPackageIndex(package.Id);
                Assert.True(packageIndex >= 0);
                Assert.Equal(package.Id, store.GetPackage(packageIndex).Id);
                Assert.Contains(package.Id, store.GetPackageIdentities());
                Assert.Equal(1, store.PackageCount);
            }
        }

        [Fact]
        public void ReAddingSameIdentityDoesNotDuplicate()
        {
            using var tempPath = new TempStorePath();
            var package = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new[] { package });
            store.AddPackages(new[] { package });

            Assert.Equal(1, store.PackageCount);
        }

        [Fact]
        public void IdentityAndPackageCountSurviveCloseAndReopen()
        {
            using var tempPath = new TempStorePath();
            var package = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using (var store = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                store.AddPackages(new[] { package });
                store.Flush();
            }

            using (var reopened = SQLitePackageStore.OpenOrCreate(tempPath.Path))
            {
                Assert.Equal(1, reopened.PackageCount);
                Assert.True(reopened.ContainsPackage(package.Id));
                Assert.Single(reopened.GetPackageIdentities());
                Assert.Equal(package.Id, reopened.GetPackage(package.Id).Id);
            }
        }

        [Fact]
        public void MultiplePackagesEnumerateInIndexOrder()
        {
            using var tempPath = new TempStorePath();
            var first = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var second = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { first, second });

            Assert.Equal(2, store.PackageCount);
            var identities = store.GetPackageIdentities();
            Assert.Contains(first.Id, identities);
            Assert.Contains(second.Id, identities);
        }

        [Fact]
        public void GetPendingPackagesReturnsAddedPackagesUntilFlush()
        {
            using var tempPath = new TempStorePath();
            var first = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var second = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { first, second });

            var pending = store.GetPendingPackages();
            Assert.Equal(2, pending.Count);
            Assert.Contains(pending, p => p.Id.Equals(first.Id));
            Assert.Contains(pending, p => p.Id.Equals(second.Id));

            store.Flush();

            Assert.Empty(store.GetPendingPackages());
        }
    }
}
