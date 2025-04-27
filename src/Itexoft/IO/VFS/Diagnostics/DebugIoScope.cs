// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.VFS.Diagnostics;

internal static class DebugIoScope
{
#if DEBUG
    private static readonly AsyncLocal<string?> CurrentName = new();

    public static string? Current => CurrentName.Value;

    public static IDisposable Begin(string name)
    {
        var previous = CurrentName.Value;
        CurrentName.Value = name;

        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? previous;
        private int disposed;

        public Scope(string? previous) => this.previous = previous;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
                return;
            CurrentName.Value = this.previous;
        }
    }
#else
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public static string? Current => null;

    public static IDisposable Begin(string name) => NullScope.Instance;
#endif
}