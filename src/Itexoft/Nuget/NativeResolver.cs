// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Itexoft.Collections;

namespace Itexoft.Nuget;

public static class NativeResolver
{
    private static readonly AtomicSet<ResolverKey> registeredAssemblies = [];
    private static readonly string currentDirectory = Path.GetDirectoryName(typeof(NativeResolver).Assembly.Location)!;
    private static readonly string? toolsDirectory = GetPackageRoot();
    private static readonly string runtimeIdentifier = GetRuntimeIdentifier();

    public static void Register(string libraryName, Assembly assembly)
    {
        if (registeredAssemblies.Add(new(libraryName, assembly)))
            NativeLibrary.SetDllImportResolver(assembly, ResolveLib);
    }

    public static string? ResolveLibPath(string name) => ResolvePath(false, name);
    public static string? ResolveExePath(string name) => ResolvePath(true, name);
    public static string? ResolveToolLibPath(string name) => ResolvePath(false, name, toolsDirectory);
    public static string? ResolveToolExePath(string name) => ResolvePath(true, name, toolsDirectory);

    private static string? ResolvePath(bool executable, string name) => ResolvePath(executable, name, currentDirectory);

    private static string? ResolvePath(bool executable, string name, string? packageRoot)
    {
        if (packageRoot == null)
            return null;

        var fileName = Path.ChangeExtension(name, GetFileExt(executable));

        return Path.Combine(packageRoot, "runtimes", runtimeIdentifier, "native", fileName);
    }

    [DebuggerStepThrough]
    private static string? GetPackageRoot()
    {
        var dir = new DirectoryInfo(currentDirectory);

        while (dir is not null && !dir.Name.Equals("tools", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;

        return dir?.Parent?.FullName;
    }

    [DebuggerStepThrough]
    private static nint ResolveLib(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        registeredAssemblies.Contains(new(libraryName, assembly))
            ? NativeLibrary.Load(ResolvePath(false, libraryName, Path.GetDirectoryName(assembly.Location))!, assembly, searchPath)
            : nint.Zero;

    [DebuggerStepThrough]
    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                Architecture.Arm => "win-arm",
                _ => throw new PlatformNotSupportedException("Unsupported Windows architecture"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.X86 => "linux-x86",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => throw new PlatformNotSupportedException("Unsupported Linux architecture"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException("Unsupported macOS architecture"),
            };
        }

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    [DebuggerStepThrough]
    private static string? GetFileExt(bool executable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return executable ? "exe" : "dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return executable ? null : "so";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return executable ? null : "dylib";

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    [DebuggerStepThrough]
    private sealed class ResolverKey(string libraryName, Assembly assembly) : IEquatable<ResolverKey>
    {
        public string LibraryName { get; } = libraryName;
        public Assembly Assembly { get; } = assembly;

        public bool Equals(ResolverKey? other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return this.LibraryName == other.LibraryName && this.Assembly.Equals(other.Assembly);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj.GetType() != this.GetType())
                return false;

            return this.Equals((ResolverKey)obj);
        }

        public override int GetHashCode() => HashCode.Combine(this.LibraryName, this.Assembly);
    }
}
