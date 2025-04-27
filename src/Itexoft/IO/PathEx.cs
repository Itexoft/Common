// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;

namespace Itexoft.IO;

public static class PathEx
{
    public static string NormalizeHomePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var expanded = rawPath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory;

            expanded = Path.Combine(home, expanded.TrimStart('~').TrimStart(Path.DirectorySeparatorChar));
        }

        expanded = Environment.ExpandEnvironmentVariables(expanded);

        return Path.GetFullPath(expanded);
    }

    public static string DenormalizeHomePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var fullPath = Path.GetFullPath(rawPath);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(home))
            return fullPath;

        var homeFull = Path.GetFullPath(home)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (fullPath.Equals(homeFull, comparison))
            return "~";

        var prefix = homeFull + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, comparison))
            return fullPath;

        var relative = fullPath.Substring(homeFull.Length);
        if (relative.Length > 0 && (relative[0] == Path.DirectorySeparatorChar || relative[0] == Path.AltDirectorySeparatorChar))
            relative = relative.Substring(1);

        return "~" + Path.DirectorySeparatorChar + relative;
    }

    public static string? ToAbsolute(string? rawPath)
    {
        return ToAbsolute(rawPath, () => Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());
    }

    public static string? ToAbsolute(string? rawPath, Assembly assembly) => ToAbsolute(rawPath, () => assembly);

    public static string? ToAbsolute(string? rawPath, Func<Assembly> assembly)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var expanded = rawPath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory;

            expanded = Path.Combine(home, expanded.TrimStart('~').TrimStart(Path.DirectorySeparatorChar));

            return Path.GetFullPath(expanded);
        }

        if (Path.IsPathRooted(expanded))
            return Path.GetFullPath(expanded);

        var baseDir = GetAssemblyBaseDirectory(assembly);
        var combined = Path.Combine(baseDir, expanded);

        return Path.GetFullPath(combined);
    }

    public static string? ToRelative(string? rawPath)
    {
        return ToRelative(rawPath, static () => Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());
    }

    public static string? ToRelative(string? rawPath, Assembly assembly) => ToRelative(rawPath, () => assembly);

    public static string? ToRelative(string? rawPath, Func<Assembly> assembly)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var fullPath = Path.GetFullPath(rawPath);
        var best = fullPath;

        var denorm = DenormalizeHomePath(fullPath);
        if (!string.IsNullOrWhiteSpace(denorm) && denorm.Length < best.Length)
            best = denorm;

        var baseDir = GetAssemblyBaseDirectory(assembly);
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            var relative = Path.GetRelativePath(baseDir, fullPath);
            if (!string.IsNullOrWhiteSpace(relative) && relative.Length < best.Length)
                best = relative;
        }

        return best;
    }

    private static string GetAssemblyBaseDirectory(Func<Assembly> assembly)
    {
        var baseDir = AppContext.BaseDirectory;

        if (assembly != null)
        {
            var location = assembly().Location;
            if (!string.IsNullOrWhiteSpace(location))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(location));
                if (!string.IsNullOrWhiteSpace(dir))
                    baseDir = dir;
            }
        }

        return Path.GetFullPath(baseDir);
    }
}