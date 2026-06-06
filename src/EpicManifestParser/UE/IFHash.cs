using System.Diagnostics.CodeAnalysis;

namespace EpicManifestParser.UE;
// ReSharper disable UseSymbolAlias

/// <summary>
/// Defines a generic interface for value types that represent a hash and support equality comparison and formatted output.
/// </summary>
/// <typeparam name="T">The value type that implements the hash interface.</typeparam>
public interface IFHash<T> where T : struct, IFHash<T>, IEquatable<T>, ISpanFormattable, IUtf8SpanFormattable
{
	/// <summary>
	/// The size of the hash/struct.
	/// </summary>
	static abstract int Size { get; }

	/// <summary>
	/// Gets a span over the bytes of the current hash value.
	/// </summary>
	/// <returns>A span representing the underlying bytes of this hash value.</returns>
	/// <remarks>
	/// The returned span refers to storage owned by the current instance and is intended for short-lived use.
	/// </remarks>
	[UnscopedRef]
	Span<byte> GetSpan();

	/// <summary>
	/// Determines whether two hash values are equal.
	/// </summary>
	/// <param name="left">The first hash value to compare.</param>
	/// <param name="right">The second hash value to compare.</param>
	/// <returns><see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> represent the same hash value; otherwise, <see langword="false"/>.</returns>
	static abstract bool operator ==(T left, T right);

	/// <summary>
	/// Determines whether two hash values are not equal.
	/// </summary>
	/// <param name="left">The first hash value to compare.</param>
	/// <param name="right">The second hash value to compare.</param>
	/// <returns><see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> represent different hash values; otherwise, <see langword="false"/>.</returns>
	static abstract bool operator !=(T left, T right);

	/// <summary>
	/// Computes the hash value for the specified data.
	/// </summary>
	/// <param name="data">The data to hash.</param>
	/// <returns>The computed hash value.</returns>
	static abstract T Compute(ReadOnlySpan<byte> data);

	/// <summary>
	/// Computes the hash value for the specified text.
	/// </summary>
	/// <param name="text">The text to hash.</param>
	/// <returns>The computed hash value.</returns>
	/// <remarks>
	/// The text does not get re-encoded.
	/// </remarks>
	static abstract T Compute(ReadOnlySpan<char> text);

	/// <summary>
	/// Asynchronously computes the hash value for the data read from the specified <see cref="Stream"/>.
	/// </summary>
	/// <param name="stream">The stream containing the data to hash.</param>
	/// <param name="cancellationToken">
	///   The token to monitor for cancellation requests.
	///   The default value is <see cref="System.Threading.CancellationToken.None" />.
	/// </param>
	/// <returns>
	/// A task that represents the asynchronous hash computation operation. The task result contains the computed hash value.
	/// </returns>
	static abstract Task<T> ComputeAsync(Stream stream, CancellationToken cancellationToken = default);

	/// <summary>
	/// Asynchronously computes the hash value for the contents of the specified file.
	/// </summary>
	/// <param name="filePath">The path to the file whose contents should be hashed.</param>
	/// <param name="cancellationToken">
	///   The token to monitor for cancellation requests.
	///   The default value is <see cref="System.Threading.CancellationToken.None" />.
	/// </param>
	/// <returns>
	/// A task that represents the asynchronous hash computation operation. The task result contains the computed hash value.
	/// </returns>
	static abstract Task<T> ComputeAsync(string filePath, CancellationToken cancellationToken = default);

	/// <summary>Returns a <see cref="string"/> representation of this hash value.</summary>
	/// <param name="upperCase">Whether or not to return an uppercase string.</param>
	/// <returns>The value of this hash, represented as a series of hexadecimal digits.</returns>
	string ToString(bool upperCase);

	/// <summary>Returns a <see cref="string"/> representation of this hash value.</summary>
	/// <returns>The value of this hash, represented as a series of uppercase hexadecimal digits.</returns>
	string ToString();

	/// <summary>Returns a <see cref="string"/> representation of the current hash instance, according to the provided format specifier.</summary>
	/// <param name="format">A read-only span containing the character representing one of the following specifiers that indicates the exact format to use when interpreting input:<br/>
	/// "x" or "X".<br/>
	/// When <paramref name="format"/> is <see langword="null"/> or empty, "X" is used.
	/// </param>
	/// <param name="formatProvider">Unused, pass a null reference.</param>
	/// <returns>The value of this hash, represented as a series of hexadecimal digits in the specified format.</returns>
	/// <exception cref="FormatException">If an invalid format is used.</exception>
	string ToString(string? format, IFormatProvider? formatProvider = null);

	/// <summary>
	/// Tries to format the current hash instance into the provided character span.
	/// </summary>
	/// <param name="destination">The span in which to write the hash as a span of characters.</param>
	/// <param name="charsWritten">When this method returns, contains the number of characters written into the span.</param>
	/// <param name="format">A read-only span containing the character representing one of the following specifiers that indicates the exact format to use when interpreting input:<br/>
	/// "x" or "X".<br/>
	/// When <paramref name="format"/> is empty, "X" is used.
	/// </param>
	/// <param name="provider">Unused, pass a null reference.</param>
	/// <returns><see langword="true"></see> if the formatting was successful; otherwise, <see langword="false"></see>.</returns>
	/// <exception cref="FormatException">If an invalid format is used.</exception>
	bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default,
		IFormatProvider? provider = null);

	/// <summary>
	/// Tries to format the current hash instance into the provided utf8 byte span.
	/// </summary>
	/// <param name="utf8Destination">The span in which to write the hash as a span of utf8 bytes.</param>
	/// <param name="bytesWritten">When this method returns, contains the number of bytes written into the span.</param>
	/// <param name="format">A read-only span containing the character representing one of the following specifiers that indicates the exact format to use when interpreting input:<br/>
	/// "x" or "X".<br/>
	/// When <paramref name="format"/> is empty, "X" is used.
	/// </param>
	/// <param name="provider">Unused, pass a null reference.</param>
	/// <returns><see langword="true"></see> if the formatting was successful; otherwise, <see langword="false"></see>.</returns>
	/// <exception cref="FormatException">If an invalid format is used.</exception>
	bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default,
		IFormatProvider? provider = null);
}
