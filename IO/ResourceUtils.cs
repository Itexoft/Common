using System.Reflection;

namespace Itexoft.Common.IO;

public static class ResourceUtils
{
    public static async Task<string> WriteManifestResourceAsync(this Assembly assembly, string resourceName, string outputPath, CancellationToken cancellationToken = default)
    {
        var name = assembly.GetManifestResourceNames().Single(n => string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase));
        var targetPath = Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, outputPath);
        await using var resource = assembly.GetManifestResourceStream(name);
        if (resource == null)
            throw new IOException($"Resource {resourceName} does not exist.");
        
        await using var file = File.Create(targetPath, 81920, FileOptions.Asynchronous);
        await resource.CopyToAsync(file, cancellationToken);
        return targetPath;
    }
}