// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.DotNET;

internal static class Sr
{
    internal const string concurrentDictionaryConcurrencyLevelMustBePositiveOrNegativeOne = "Concurrency level must be positive or -1.";
    internal const string concurrentDictionaryArrayNotLargeEnough = "The array is not large enough to copy the elements.";
    internal const string concurrentDictionarySourceContainsDuplicateKeys = "Source contains duplicate keys.";
    internal const string argKeyNotFoundWithKey = "The given key '{0}' was not present in the dictionary.";
    internal const string concurrentDictionaryKeyAlreadyExisted = "An item with the same key has already been added.";
    internal const string concurrentDictionaryItemKeyIsNull = "The item key is null.";
    internal const string concurrentDictionaryTypeOfKeyIncorrect = "The type of the key is incorrect.";
    internal const string concurrentDictionaryArrayIncorrectType = "The array is of incorrect type.";
    internal const string concurrentCollectionSyncRootNotSupported = "SyncRoot is not supported on ConcurrentCollections.";
    internal const string concurrentDictionaryIncompatibleComparer = "The comparer is not compatible with the key type.";

    internal const string argumentOutOfRangeIndex = "Index must be non-negative and less than the size of the array.";
    internal const string argumentOutOfRangeCount = "Count must be non-negative and less than the size of the array.";
    internal const string argumentInvalidOffLen = "Offset and length were out of bounds for the array.";

    internal static string Format(string resourceFormat, params object?[] args) => string.Format(resourceFormat, args);
}
