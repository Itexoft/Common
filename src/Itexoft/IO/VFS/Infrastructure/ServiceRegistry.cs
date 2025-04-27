// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Itexoft.IO.VFS.Infrastructure;

internal sealed class ServiceRegistry : IServiceProvider
{
    private readonly ConcurrentDictionary<Type, object> services = new();

    public object? GetService(Type serviceType)
    {
        this.services.TryGetValue(serviceType, out var value);

        return value;
    }

    public void Add<TService>(TService instance)
        where TService : class
    {
        if (!this.services.TryAdd(typeof(TService), instance))
            throw new InvalidOperationException($"Service {typeof(TService).Name} already registered.");
    }

    public bool TryGet<TService>([MaybeNullWhen(false)] out TService service)
        where TService : class
    {
        if (this.services.TryGetValue(typeof(TService), out var value) && value is TService typed)
        {
            service = typed;

            return true;
        }

        service = null;

        return false;
    }
}