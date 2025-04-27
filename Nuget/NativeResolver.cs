using Itexoft.Common.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Itexoft.Common.Nuget;

internal static class NativeResolver
{
    private static readonly ConcurrentSet<ResolverKey> registeredAssemblies = [];
    private static readonly string currentDirectory = Path.GetDirectoryName(typeof(NativeResolver).Assembly.Location)!;
    private static readonly string runtimeIdentifier = GetRuntimeIdentifier();

    public static void Register(string libraryName, Assembly assembly)
    {
        if (registeredAssemblies.Add(new(libraryName, assembly)))
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveLib);
        }
    }

    public static string ResolveLibPath(string name, Assembly? assembly = null) => ResolvePath(false, name, assembly);
    public static string ResolveExePath(string name, Assembly? assembly = null) => ResolvePath(true, name, assembly);

    private static string ResolvePath(bool executable, string name, Assembly? assembly)
    {
        var packageRoot = assembly == null ? currentDirectory : Path.GetDirectoryName(assembly.Location)!;
        var fileName = Path.ChangeExtension(name, GetFileExt(executable));
        return Path.Combine(packageRoot, "runtimes", runtimeIdentifier, "native", fileName);
    }

    [DebuggerStepThrough]
    private static IntPtr ResolveLib(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (registeredAssemblies.Contains(new ResolverKey(libraryName, assembly)))
        {
            return NativeLibrary.Load(ResolveLibPath(libraryName, assembly), assembly, searchPath);
        }

        return IntPtr.Zero;
    }

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
                _ => throw new PlatformNotSupportedException("Unsupported Windows architecture")
            };
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => throw new PlatformNotSupportedException("Unsupported Linux architecture")
            };
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException("Unsupported macOS architecture")
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
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return this.LibraryName == other.LibraryName && this.Assembly.Equals(other.Assembly);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return this.Equals((ResolverKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(this.LibraryName, this.Assembly);
        }
    }
}