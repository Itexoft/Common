// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Infrastructure;

namespace Itexoft.IO.VFS.Features;

internal interface IContainerFeature
{
    FeatureKind Kind { get; }
    void Attach(FeatureContext context);
}

internal enum FeatureKind
{
    Allocation,
    DirectoryIndex,
    FileTable,
    AttributeTable,
    LockManager,
    PageCache,
    Diagnostics
}

internal readonly struct FeatureContext
{
    private readonly ServiceRegistry registry;

    public FeatureContext(ServiceRegistry registry) => this.registry = registry;

    public IServiceProvider Services => this.registry;

    public void Register<TService>(TService instance)
        where TService : class
        => this.registry.Add(instance);

    public TService GetRequiredService<TService>() where TService : class
        => (TService)(this.registry.GetService(typeof(TService))
                      ?? throw new InvalidOperationException($"Service {typeof(TService).Name} not available."));
}

internal sealed class FeatureRegistry
{
    private readonly Dictionary<FeatureKind, IContainerFeature> features = new();
    private readonly List<IContainerFeature> ordered = [];

    public void Register(IContainerFeature feature)
    {
        if (this.features.ContainsKey(feature.Kind))
            throw new InvalidOperationException($"Feature {feature.Kind} already registered.");
        this.features[feature.Kind] = feature;
        this.ordered.Add(feature);
    }

    public TFeature Get<TFeature>(FeatureKind kind) where TFeature : class, IContainerFeature
    {
        if (!this.features.TryGetValue(kind, out var feature))
            throw new InvalidOperationException($"Feature {kind} not available.");
        if (feature is not TFeature typed)
            throw new InvalidOperationException($"Feature {kind} has unexpected type {feature.GetType().Name}.");

        return typed;
    }

    public void AttachAll(FeatureContext context)
    {
        foreach (var feature in this.ordered)
            feature.Attach(context);
    }

    public bool Contains(FeatureKind kind) => this.features.ContainsKey(kind);
}