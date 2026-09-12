// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.PackageGraph.MicrosoftUpdate.Endpoints.ClientSync;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.ObjectModel;
using Microsoft.PackageGraph.Storage;
using Microsoft.PackageGraph.Storage.Local;
using Microsoft.UpdateServices.WebServices.ClientSync;
using Xunit;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    public class ClientSyncWebServiceTests
    {
        private static void AddAndPublish(string path, params IPackage[] packages)
        {
            using var store = SQLitePackageStore.OpenOrCreate(path);
            store.AddPackages(packages);
            store.Flush();
        }

        private static Guid Id(SoftwareUpdate update) => ((MicrosoftUpdatePackageIdentity)update.Id).ID;

        private static ClientSyncWebService CreateService(
            IClientSyncMetadataStore metadataSource,
            Microsoft.Extensions.Logging.ILogger? logger = null)
        {
            var service = new ClientSyncWebService();
            service.SetContentURLBase(null);
            service.SetServiceConfiguration(new Config());
            service.SetPackageStore(metadataSource);
            if (logger != null)
            {
                service.SetLogger(logger);
            }

            return service;
        }

        [Fact]
        public async Task GetConfigAsyncReturnsConfiguredConfig()
        {
            using var tempPath = new TempStorePath();
            AddAndPublish(tempPath.Path);
            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);

            var config = await service.GetConfigAsync("1.20");

            Assert.NotNull(config);
        }

        [Fact]
        public async Task GetCookieAsyncReturnsNonNullFutureCookie()
        {
            using var tempPath = new TempStorePath();
            AddAndPublish(tempPath.Path);
            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);

            var cookie = await service.GetCookieAsync(
                Array.Empty<AuthorizationCookie>(),
                null,
                DateTime.MinValue,
                DateTime.Now,
                "1.20");

            Assert.NotNull(cookie);
            Assert.True(cookie.Expiration > DateTime.Now);
        }

        [Fact]
        public async Task SyncUpdatesReturnsLeafWhenPrerequisiteReportedInstalled()
        {
            using var tempPath = new TempStorePath();
            var detectoid = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var detectoidId = Id(detectoid);
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, detectoid, leaf);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var detectoidRevisionId = metadataSource.GetRevisionId(detectoid.Id);
            var leafRevisionId = metadataSource.GetRevisionId(leaf.Id);
            var service = CreateService(metadataSource);

            var response = await service.SyncUpdatesAsync(
                new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() },
                new SyncUpdateParameters
                {
                    InstalledNonLeafUpdateIDs = new[] { detectoidRevisionId }
                });

            Assert.NotNull(response.NewUpdates);
            var offered = Assert.Single(response.NewUpdates);
            Assert.Equal(leafRevisionId, offered.ID);
            // Response.Truncated only reflects the SQL truncation flag at the Leaf
            // stage; earlier stages always force another round-trip (see
            // ClientSync_Software.cs AddSoftwareStage). One candidate under
            // MaxUpdatesInResponse at the Leaf stage means false here.
            Assert.False(response.Truncated);
        }

        [Fact]
        public async Task SyncUpdatesExcludesCandidateAlreadyReportedAsOtherCached()
        {
            using var tempPath = new TempStorePath();
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            AddAndPublish(tempPath.Path, leaf);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var leafRevisionId = metadataSource.GetRevisionId(leaf.Id);
            var service = CreateService(metadataSource);

            var response = await service.SyncUpdatesAsync(
                new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() },
                new SyncUpdateParameters
                {
                    OtherCachedUpdateIDs = new[] { leafRevisionId }
                });

            Assert.True(response.NewUpdates == null || response.NewUpdates.Length == 0);
        }

        [Fact]
        public async Task SyncUpdatesGatesUnapprovedSoftwareUpdateUnlessExplicitlyApproved()
        {
            using var tempPath = new TempStorePath();
            var detectoid = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec());
            var detectoidId = Id(detectoid);
            var leaf = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                SimplePrerequisites = { detectoidId }
            });
            AddAndPublish(tempPath.Path, detectoid, leaf);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var detectoidRevisionId = metadataSource.GetRevisionId(detectoid.Id);
            var leafRevisionId = metadataSource.GetRevisionId(leaf.Id);
            var service = CreateService(metadataSource);

            var parameters = new SyncUpdateParameters
            {
                InstalledNonLeafUpdateIDs = new[] { detectoidRevisionId }
            };
            var cookie = new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() };

            // AddApprovedSoftwareUpdates([]) switches the service out of "approve
            // all" mode even with zero entries -- this is the gate under test.
            service.AddApprovedSoftwareUpdates(Array.Empty<MicrosoftUpdatePackageIdentity>());
            var unapprovedResponse = await service.SyncUpdatesAsync(cookie, parameters);
            Assert.True(unapprovedResponse.NewUpdates == null || unapprovedResponse.NewUpdates.Length == 0);

            service.AddApprovedSoftwareUpdate((MicrosoftUpdatePackageIdentity)leaf.Id);
            var approvedResponse = await service.SyncUpdatesAsync(cookie, parameters);
            Assert.NotNull(approvedResponse.NewUpdates);
            var offered = Assert.Single(approvedResponse.NewUpdates);
            Assert.Equal(leafRevisionId, offered.ID);
        }

        [Fact]
        public async Task GetExtendedUpdateInfo2AsyncIgnoresStaleIdentityAndReturnsKnownOne()
        {
            using var tempPath = new TempStorePath();
            var known = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                Title = "Known Update Title"
            });
            AddAndPublish(tempPath.Path, known);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);
            var knownIdentity = (MicrosoftUpdatePackageIdentity)known.Id;
            var staleIdentity = new UpdateIdentity { UpdateID = Guid.NewGuid(), RevisionNumber = 1 };

            var result = await service.GetExtendedUpdateInfo2Async(
                cookie: null,
                updateIDs: new[]
                {
                    new UpdateIdentity { UpdateID = knownIdentity.ID, RevisionNumber = knownIdentity.Revision },
                    staleIdentity
                },
                infoTypes: new[] { XmlUpdateFragmentType.LocalizedProperties },
                locales: new[] { "en" },
                deviceAttributes: null);

            Assert.NotNull(result.Updates);
            var update = Assert.Single(result.Updates);
            Assert.Contains("Known Update Title", update.Xml);
        }

        [Fact]
        public async Task StaleIdentityWarningReachesConfiguredLogger()
        {
            using var tempPath = new TempStorePath();
            AddAndPublish(tempPath.Path);
            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var logger = new CapturingLogger();
            var service = CreateService(metadataSource, logger);
            var staleIdentity = new UpdateIdentity { UpdateID = Guid.NewGuid(), RevisionNumber = 1 };

            await service.GetExtendedUpdateInfo2Async(
                cookie: null,
                updateIDs: new[] { staleIdentity },
                infoTypes: new[] { XmlUpdateFragmentType.LocalizedProperties },
                locales: new[] { "en" },
                deviceAttributes: null);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
            Assert.Contains("stale client update identity", entry.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetFileLocationsAsyncResolvesKnownDigestAndIgnoresUnknownOne()
        {
            using var tempPath = new TempStorePath();
            const string digestBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            var withFile = SyntheticUpdates.BuildSoftwareUpdate(new SyntheticUpdates.SoftwareUpdateSpec
            {
                Files = { (digestBase64, "payload.cab") }
            });
            AddAndPublish(tempPath.Path, withFile);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);

            var result = await service.GetFileLocationsAsync(
                new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() },
                new[] { Convert.FromBase64String(digestBase64), new byte[20] });

            var resolved = Assert.Single(result.FileLocations);
            Assert.Equal(Convert.FromBase64String(digestBase64), resolved.FileDigest);
            Assert.Equal("http://mu/payload.cab", resolved.Url);
        }

        [Fact]
        public async Task SyncUpdatesWithSkipSoftwareSyncMatchesDriverByHardwareId()
        {
            using var tempPath = new TempStorePath();
            var driver = SyntheticUpdates.BuildDriverUpdate(new SyntheticUpdates.DriverUpdateSpec
            {
                HardwareId = "pci\\ven_dead&dev_beef"
            });
            AddAndPublish(tempPath.Path, driver);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var driverRevisionId = metadataSource.GetRevisionId(driver.Id);
            var service = CreateService(metadataSource);

            var response = await service.SyncUpdatesAsync(
                new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() },
                new SyncUpdateParameters
                {
                    SkipSoftwareSync = true,
                    SystemSpec = new[]
                    {
                        new Device { HardwareIDs = new[] { "PCI\\VEN_DEAD&DEV_BEEF" } }
                    }
                });

            Assert.NotNull(response.NewUpdates);
            var offered = Assert.Single(response.NewUpdates);
            Assert.Equal(driverRevisionId, offered.ID);
        }

        [Fact]
        public async Task SyncUpdatesWithSkipSoftwareSyncFindsNoMatchForUnrelatedHardwareId()
        {
            using var tempPath = new TempStorePath();
            var driver = SyntheticUpdates.BuildDriverUpdate(new SyntheticUpdates.DriverUpdateSpec
            {
                HardwareId = "pci\\ven_dead&dev_beef"
            });
            AddAndPublish(tempPath.Path, driver);

            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);

            var response = await service.SyncUpdatesAsync(
                new Cookie { Expiration = DateTime.Now.AddDays(1), EncryptedData = Array.Empty<byte>() },
                new SyncUpdateParameters
                {
                    SkipSoftwareSync = true,
                    SystemSpec = new[]
                    {
                        new Device { HardwareIDs = new[] { "PCI\\VEN_0000&DEV_0000" } }
                    }
                });

            Assert.True(response.NewUpdates == null || response.NewUpdates.Length == 0);
        }

        [Fact]
        public async Task RegisterComputerAsyncIsNotImplemented()
        {
            using var tempPath = new TempStorePath();
            AddAndPublish(tempPath.Path);
            using var metadataSource = PackageStore.OpenClientSync(tempPath.Path);
            var service = CreateService(metadataSource);

            await Assert.ThrowsAsync<NotImplementedException>(
                () => service.RegisterComputerAsync(null, new ComputerInfo()));
        }
    }
}
