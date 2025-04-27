// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Cache;
using Itexoft.IO.VFS.Locking;
using Itexoft.IO.VFS.Metadata;
using Itexoft.IO.VFS.Metadata.Attributes;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.Features;

internal static class FeatureDefaults
{
    public static void Apply(FeatureRegistry registry)
    {
        if (!registry.Contains(FeatureKind.PageCache))
            registry.Register(new PageCacheFeature());
        if (!registry.Contains(FeatureKind.Allocation))
            registry.Register(new AllocationFeature());
        if (!registry.Contains(FeatureKind.FileTable))
            registry.Register(new FileTableFeature());
        if (!registry.Contains(FeatureKind.DirectoryIndex))
            registry.Register(new DirectoryIndexFeature());
        if (!registry.Contains(FeatureKind.AttributeTable))
            registry.Register(new AttributeTableFeature());
        if (!registry.Contains(FeatureKind.LockManager))
            registry.Register(new LockManagerFeature());
    }

    private sealed class PageCacheFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.PageCache;

        public void Attach(FeatureContext context)
        {
            var storage = context.GetRequiredService<StorageEngine>();
            var cache = new PageCache(storage.PageSize);
            context.Register(cache);
        }
    }

    private sealed class AllocationFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.Allocation;

        public void Attach(FeatureContext context)
        {
            var storage = context.GetRequiredService<StorageEngine>();
            var allocator = new ExtentAllocator(storage);
            context.Register(allocator);
        }
    }

    private sealed class FileTableFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.FileTable;

        public void Attach(FeatureContext context)
        {
            var allocator = context.GetRequiredService<ExtentAllocator>();
            var fileTable = new FileTable(allocator);
            context.Register(fileTable);
        }
    }

    private sealed class DirectoryIndexFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.DirectoryIndex;

        public void Attach(FeatureContext context)
        {
            var fileTable = context.GetRequiredService<FileTable>();
            var directoryIndex = new DirectoryIndex(fileTable);
            context.Register(directoryIndex);
        }
    }

    private sealed class AttributeTableFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.AttributeTable;

        public void Attach(FeatureContext context)
        {
            var fileTable = context.GetRequiredService<FileTable>();
            var attributeTable = new AttributeTable(fileTable);
            context.Register(attributeTable);
        }
    }

    private sealed class LockManagerFeature : IContainerFeature
    {
        public FeatureKind Kind => FeatureKind.LockManager;

        public void Attach(FeatureContext context)
        {
            var lockManager = new LockManager();
            context.Register(lockManager);
        }
    }
}