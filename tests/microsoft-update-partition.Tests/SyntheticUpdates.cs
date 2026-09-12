// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata;
using Microsoft.PackageGraph.MicrosoftUpdate.Metadata.Content;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    internal static class SyntheticUpdates
    {
        // ClientSyncCoreFragmentBuilder.Build (exercised by AddPackages) reads
        // decompressed metadata as UTF-16LE unconditionally, matching how real
        // upstream data arrives (InMemoryUpdateFactory encodes with Unicode
        // before gzip-compressing) -- UTF-8 bytes fail to parse there.
        private static byte[] Gzip(string xml)
        {
            var raw = Encoding.Unicode.GetBytes(xml);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return output.ToArray();
        }

        private static string XmlEscape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        internal sealed class SoftwareUpdateSpec
        {
            public Guid UpdateId = Guid.NewGuid();
            public int Revision = 1;
            public string Title = "Synthetic Software Update";
            public string KBArticleId = "1234567";
            public List<Guid> SimplePrerequisites = new();
            public List<(bool IsCategory, List<Guid> Members)> AtLeastOnePrerequisites = new();
            public List<Guid> SupersededUpdates = new();
            public List<(Guid Id, int Revision)> BundledUpdates = new();
            public List<(string Digest, string FileName)> Files = new();
        }

        internal static SoftwareUpdate BuildSoftwareUpdate(SoftwareUpdateSpec spec)
        {
            var prerequisitesXml = new StringBuilder();
            foreach (var simple in spec.SimplePrerequisites)
            {
                prerequisitesXml.Append($@"<upd:UpdateIdentity UpdateID=""{simple}"" RevisionNumber=""0"" />");
            }

            foreach (var (isCategory, members) in spec.AtLeastOnePrerequisites)
            {
                prerequisitesXml.Append($@"<upd:AtLeastOne IsCategory=""{(isCategory ? "true" : "false")}"">");
                foreach (var member in members)
                {
                    prerequisitesXml.Append($@"<upd:UpdateIdentity UpdateID=""{member}"" RevisionNumber=""0"" />");
                }

                prerequisitesXml.Append("</upd:AtLeastOne>");
            }

            var supersededXml = new StringBuilder();
            foreach (var supersededId in spec.SupersededUpdates)
            {
                supersededXml.Append($@"<upd:UpdateIdentity UpdateID=""{supersededId}"" RevisionNumber=""0"" />");
            }

            var bundledXml = new StringBuilder();
            foreach (var (id, revision) in spec.BundledUpdates)
            {
                bundledXml.Append($@"<upd:UpdateIdentity UpdateID=""{id}"" RevisionNumber=""{revision}"" />");
            }

            var filesXml = new StringBuilder();
            var filesCollection = new Dictionary<string, UpdateFileUrl>();
            foreach (var (digest, fileName) in spec.Files)
            {
                filesXml.Append($@"<upd:File FileName=""{fileName}"" Digest=""{digest}"" DigestAlgorithm=""SHA1"" Size=""100"" Modified=""2024-01-01T00:00:00Z"" PatchingType=""full"" />");
                filesCollection[digest] = new UpdateFileUrl(digest, $"http://mu/{fileName}", null);
            }

            var xml = $@"<?xml version=""1.0"" encoding=""utf-16""?>
<upd:Update xmlns:upd=""http://schemas.microsoft.com/msus/2002/12/Update"">
  <upd:UpdateIdentity UpdateID=""{spec.UpdateId}"" RevisionNumber=""{spec.Revision}"" />
  <upd:Properties UpdateType=""Software"">
    <upd:SupportUrl>http://example/support</upd:SupportUrl>
    <upd:KBArticleID>{spec.KBArticleId}</upd:KBArticleID>
  </upd:Properties>
  <upd:LocalizedPropertiesCollection>
    <upd:LocalizedProperties>
      <upd:Language>en</upd:Language>
      <upd:Title>{spec.Title}</upd:Title>
      <upd:Description>Synthetic description</upd:Description>
    </upd:LocalizedProperties>
  </upd:LocalizedPropertiesCollection>
  <upd:Relationships>
    <upd:Prerequisites>{prerequisitesXml}</upd:Prerequisites>
    <upd:SupersededUpdates>{supersededXml}</upd:SupersededUpdates>
    <upd:BundledUpdates>{bundledXml}</upd:BundledUpdates>
  </upd:Relationships>
  <upd:Files>{filesXml}</upd:Files>
</upd:Update>";

            return (SoftwareUpdate)MicrosoftUpdatePackage.FromMetadataXml(Gzip(xml), filesCollection);
        }

        internal sealed class DriverUpdateSpec
        {
            public Guid UpdateId = Guid.NewGuid();
            public int Revision = 1;
            public string Title = "Synthetic Driver Update";
            public string HardwareId = "pci\\ven_1234&dev_5678";
            public string WhqlDriverId = "111";
            public string Manufacturer = "Contoso";
            public string Company = "Contoso Inc";
            public string Provider = "Contoso Provider";
            public string Class = "Net";
            public string DriverVerDate = "2024-01-15";
            public string DriverVerVersion = "10.20.30.40";
            public List<(string OperatingSystem, byte Score)> FeatureScores = new() { ("6.4.0.0", 0xFF) };
            public List<Guid> DistributionComputerHardwareIds = new() { Guid.NewGuid() };
            public List<Guid> TargetComputerHardwareIds = new() { Guid.NewGuid() };
        }

        internal static DriverUpdate BuildDriverUpdate(DriverUpdateSpec spec)
        {
            var featureScoresXml = new StringBuilder();
            foreach (var (os, score) in spec.FeatureScores)
            {
                featureScoresXml.Append($@"<drv:FeatureScore OperatingSystem=""{os}"" FeatureScore=""{score:X2}"" />");
            }

            var distributionXml = new StringBuilder();
            foreach (var id in spec.DistributionComputerHardwareIds)
            {
                distributionXml.Append($"<drv:DistributionComputerHardwareId>{id}</drv:DistributionComputerHardwareId>");
            }

            var targetXml = new StringBuilder();
            foreach (var id in spec.TargetComputerHardwareIds)
            {
                targetXml.Append($"<drv:TargetComputerHardwareId>{id}</drv:TargetComputerHardwareId>");
            }

            var xml = $@"<?xml version=""1.0"" encoding=""utf-16""?>
<upd:Update xmlns:upd=""http://schemas.microsoft.com/msus/2002/12/Update"" xmlns:drv=""http://schemas.microsoft.com/msus/2002/12/UpdateHandlers/WindowsDriver"">
  <upd:UpdateIdentity UpdateID=""{spec.UpdateId}"" RevisionNumber=""{spec.Revision}"" />
  <upd:Properties UpdateType=""Driver"" />
  <upd:LocalizedPropertiesCollection>
    <upd:LocalizedProperties>
      <upd:Language>en</upd:Language>
      <upd:Title>{spec.Title}</upd:Title>
      <upd:Description>Synthetic driver description</upd:Description>
    </upd:LocalizedProperties>
  </upd:LocalizedPropertiesCollection>
  <upd:ApplicabilityRules>
    <upd:Metadata>
      <drv:WindowsDriverMetaData HardwareID=""{XmlEscape(spec.HardwareId)}"" WhqlDriverID=""{spec.WhqlDriverId}"" Manufacturer=""{spec.Manufacturer}"" Company=""{spec.Company}"" Provider=""{spec.Provider}"" DriverVerDate=""{spec.DriverVerDate}"" DriverVerVersion=""{spec.DriverVerVersion}"" Class=""{spec.Class}"">
        {featureScoresXml}
        {distributionXml}
        {targetXml}
      </drv:WindowsDriverMetaData>
    </upd:Metadata>
  </upd:ApplicabilityRules>
</upd:Update>";

            return (DriverUpdate)MicrosoftUpdatePackage.FromMetadataXml(Gzip(xml), null);
        }

        internal sealed class MinimalUpdateSpec
        {
            public Guid UpdateId = Guid.NewGuid();
            public int Revision = 1;
            public string Title = "Minimal Update With No Optional Properties";
        }

        internal static SoftwareUpdate BuildMinimalSoftwareUpdate(MinimalUpdateSpec spec)
        {
            var xml = $@"<?xml version=""1.0"" encoding=""utf-16""?>
<upd:Update xmlns:upd=""http://schemas.microsoft.com/msus/2002/12/Update"">
  <upd:UpdateIdentity UpdateID=""{spec.UpdateId}"" RevisionNumber=""{spec.Revision}"" />
  <upd:Properties UpdateType=""Software"" />
  <upd:LocalizedPropertiesCollection>
    <upd:LocalizedProperties>
      <upd:Language>en</upd:Language>
      <upd:Title>{spec.Title}</upd:Title>
      <upd:Description>Minimal description</upd:Description>
    </upd:LocalizedProperties>
  </upd:LocalizedPropertiesCollection>
  <upd:Relationships>
    <upd:Prerequisites></upd:Prerequisites>
    <upd:SupersededUpdates></upd:SupersededUpdates>
    <upd:BundledUpdates></upd:BundledUpdates>
  </upd:Relationships>
  <upd:Files></upd:Files>
</upd:Update>";

            return (SoftwareUpdate)MicrosoftUpdatePackage.FromMetadataXml(Gzip(xml), null);
        }
    }
}
