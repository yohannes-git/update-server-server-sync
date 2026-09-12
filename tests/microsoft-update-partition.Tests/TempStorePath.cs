// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Microsoft.PackageGraph.MicrosoftUpdate.Tests
{
    internal sealed class TempStorePath : IDisposable
    {
        public string Path { get; }

        public TempStorePath()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "upsync-tests-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
