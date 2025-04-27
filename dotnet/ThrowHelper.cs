namespace Itexoft.Common.DotNET;

internal static class ThrowHelper
{
    internal static void ThrowKeyNullException() => throw new ArgumentNullException("key");

    internal static void ThrowArgumentNullException(string paramName) => throw new ArgumentNullException(paramName);

    internal static void ThrowArgumentNullException(string paramName, string message) => throw new ArgumentNullException(paramName, message);

    internal static void ThrowIncompatibleComparer() => throw new ArgumentException(SR.ConcurrentDictionary_IncompatibleComparer);

    internal static void ThrowValueNullException() => throw new ArgumentNullException("value");
}