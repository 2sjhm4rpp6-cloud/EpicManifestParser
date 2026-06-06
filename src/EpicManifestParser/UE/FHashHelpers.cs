namespace EpicManifestParser.UE;
// ReSharper disable UseSymbolAlias

internal static class FHashHelpers
{
    public static string ToString(ReadOnlySpan<byte> hash, string? format)
    {
        if (format is null || format.Length == 0 || format == "X")
            return Convert.ToHexString(hash);
        if (format == "x")
            return Convert.ToHexStringLower(hash);
        throw new FormatException("the provided format is not valid");
    }

    public static bool TryFormat(ReadOnlySpan<byte> hash, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
    {
        if (format.IsEmpty || format is "X")
            return Convert.TryToHexString(hash, destination, out charsWritten);
        if (format is "x")
            return Convert.TryToHexStringLower(hash, destination, out charsWritten);
        throw new FormatException("the provided format is not valid");
    }

    public static bool TryFormat(ReadOnlySpan<byte> hash, Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format)
    {
        if (format.IsEmpty || format is "X")
            return Convert.TryToHexString(hash, utf8Destination, out bytesWritten);
        if (format is "x")
            return Convert.TryToHexStringLower(hash, utf8Destination, out bytesWritten);
        throw new FormatException("the provided format is not valid");
    }
}
