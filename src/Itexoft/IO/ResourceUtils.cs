// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using Itexoft.Threading;

namespace Itexoft.IO;

public static class ResourceUtils
{
    public static async Task<string> WriteManifestResourceAsync(
        this Assembly assembly,
        string resourceName,
        string outputPath,
        CancelToken cancelToken = default)
    {
        var name = assembly.GetManifestResourceNames().Single(n => string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase));

        var targetPath = Path.IsPathRooted(outputPath)
            ? outputPath
            : Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, outputPath);

        await using var resource = assembly.GetManifestResourceStream(name);

        if (resource == null)
            throw new IOException($"Resource {resourceName} does not exist.");

        using (cancelToken.Bridge(out var token))
        {
            await using var file = File.Create(targetPath, 81920, FileOptions.Asynchronous);
            await resource.CopyToAsync(file, token);
        }

        return targetPath;
    }

    public static string GetManifestResourceString(Assembly assembly, string resourceName)
    {
        var name = assembly.GetManifestResourceNames().Single(n => string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase));
        using var resource = assembly.GetManifestResourceStream(name);

        if (resource == null)
            throw new IOException($"Resource {resourceName} does not exist.");

        using var sr = new StreamReader(resource);

        return sr.ReadToEnd();
    }
}
