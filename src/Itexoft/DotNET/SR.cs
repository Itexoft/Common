// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.DotNET;

internal static class SR
{
    internal const string ConcurrentDictionary_ConcurrencyLevelMustBePositiveOrNegativeOne = "Concurrency level must be positive or -1.";
    internal const string ConcurrentDictionary_ArrayNotLargeEnough = "The array is not large enough to copy the elements.";
    internal const string ConcurrentDictionary_SourceContainsDuplicateKeys = "Source contains duplicate keys.";
    internal const string Arg_KeyNotFoundWithKey = "The given key '{0}' was not present in the dictionary.";
    internal const string ConcurrentDictionary_KeyAlreadyExisted = "An item with the same key has already been added.";
    internal const string ConcurrentDictionary_ItemKeyIsNull = "The item key is null.";
    internal const string ConcurrentDictionary_TypeOfKeyIncorrect = "The type of the key is incorrect.";
    internal const string ConcurrentDictionary_ArrayIncorrectType = "The array is of incorrect type.";
    internal const string ConcurrentCollection_SyncRoot_NotSupported = "SyncRoot is not supported on ConcurrentCollections.";
    internal const string ConcurrentDictionary_IncompatibleComparer = "The comparer is not compatible with the key type.";

    internal const string ArgumentOutOfRange_Index = "Index must be non-negative and less than the size of the array.";
    internal const string ArgumentOutOfRange_Count = "Count must be non-negative and less than the size of the array.";
    internal const string Argument_InvalidOffLen = "Offset and length were out of bounds for the array.";


    internal static string Format(string resourceFormat, params object?[] args) => string.Format(resourceFormat, args);
}