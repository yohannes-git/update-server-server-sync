// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage.Local;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class EdgeCaseTests
    {
        [Fact]
        public void PackageWithNoOptionalPropertiesRoundTripsWithoutThrowing()
        {
            using var tempPath = new TempStorePath();
            var package = SyntheticUpdates.BuildMinimalSoftwareUpdate(new SyntheticUpdates.MinimalUpdateSpec());

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { package });

            var readBack = (SoftwareUpdate)store.GetPackage(package.Id);

            Assert.Equal("Minimal Update With No Optional Properties", readBack.Title);
            Assert.Null(readBack.KBArticleId);
            Assert.Empty(readBack.Prerequisites ?? new System.Collections.Generic.List<Metadata.Prerequisites.IPrerequisite>());
            Assert.Empty(readBack.Files);
            Assert.Empty(readBack.SupersededUpdates ?? new System.Collections.Generic.List<Guid>());
        }

        [Fact]
        public void CustomKeyLookupWithNoMatchReturnsFalseCleanly()
        {
            using var tempPath = new TempStorePath();
            var package = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());

            using var store = SQLitePackageStore.OpenOrCreate(tempPath.Path);
            store.AddPackages(new IPackage[] { package });

            var readBack = (SoftwareUpdate)store.GetPackage(package.Id);

            Assert.Null(readBack.IsSupersededBy);
            Assert.Null(readBack.BundledWithUpdates);
        }
    }
}
