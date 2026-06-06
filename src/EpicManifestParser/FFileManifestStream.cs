using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

using Microsoft.Win32.SafeHandles;

using OffiUtils;

namespace EpicManifestParser;
// ReSharper disable UseSymbolAlias

/// <summary>
/// A stream representing a <see cref="FFileManifest"/>
/// </summary>
public sealed class FFileManifestStream : RandomAccessStream
{
    private readonly FFileManifest _fileManifest;
    private readonly bool _cacheAsIs;

    /// <summary>Always <see langword="true"/></summary>
    public override bool CanRead => true;
    /// <summary>Always <see langword="true"/></summary>
    public override bool CanSeek => true;
    /// <summary>Always <see langword="false"/></summary>
    public override bool CanWrite => false;
    /// <summary>Gets the length/size of the stream</summary>
    public override long Length => _fileManifest.FileSize;
    
    private long _position;

    /// <summary>Gets or sets the current position within the stream.</summary>
    public override long Position
    {
        get => _position;
        set
        {
            if ((ulong)value > (ulong)Length)
                throw new ArgumentOutOfRangeException(nameof(Position), value, "Value is negative or exceeds the stream's length");

            _position = value;
        }
    }

    /// <summary>Gets the file name of the <see cref="FFileManifest"/> represented by this stream</summary>
    public string FileName => _fileManifest.FileName;

    internal FFileManifestStream(FFileManifest fileManifest, bool cacheAsIs)
    {
        if (string.IsNullOrEmpty(fileManifest.Manifest.Options.ChunkBaseUrl))
            throw new ArgumentException("Missing ChunkBaseUrl");
        if (fileManifest.Manifest.Meta.bIsFileData)
            throw new NotSupportedException("File-data manifests are not supported");

        _fileManifest = fileManifest;
        _cacheAsIs = cacheAsIs;
    }

    /// <summary>
    /// Asynchronously saves the current stream to another stream.
    /// </summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="progressCallback">The progress change callback. (optional)</param>
    /// <param name="userState">The user state for the <paramref name="progressCallback"/>. (optional)</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public async Task SaveToAsync(Stream destination, Action<SaveProgressChangedEventArgs>? progressCallback,
        object? userState = null, int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        if (destination is MemoryStream {Position: 0} ms)
        {
            ms.Capacity = (int)Length;
            if (ms.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                await SaveBytesAsync(buffer.Array!, progressCallback, userState, maxDegreeOfParallelism, cancellationToken).ConfigureAwait(false);
                ms.Position = (int)Length;
                return;
            }
        }

        // TODO: make concurrent

        var downloadState = new DownloadState<byte[]>(null!, _fileManifest, userState, progressCallback);

        if (_cacheAsIs)
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(_fileManifest.Manifest.Options.ChunkDownloadBufferSize);

            try
            {
                foreach (FChunkPart chunkPart in _fileManifest.ChunkPartsArray)
                {
                    await chunkPart.Chunk.ReadDataAsIsAsync(poolBuffer, _fileManifest.Manifest, cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(poolBuffer, (int)chunkPart.Offset, (int)chunkPart.Size),
                        cancellationToken).ConfigureAwait(false);
                    downloadState.OnBytesWritten(chunkPart.Size);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }
        else
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(_fileManifest.Manifest.Options.ChunkDownloadBufferSize);

            try
            {
                foreach (FChunkPart chunkPart in _fileManifest.ChunkPartsArray)
                {
                    await chunkPart.Chunk.ReadDataAsync(poolBuffer, 0, (int)chunkPart.Size,
                        (int)chunkPart.Offset, _fileManifest.Manifest, cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(poolBuffer, 0, (int)chunkPart.Size),
                        cancellationToken).ConfigureAwait(false);
                    downloadState.OnBytesWritten(chunkPart.Size);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="SaveToAsync(Stream,Action{SaveProgressChangedEventArgs}?,object?,int?,CancellationToken)"/>
    public Task SaveToAsync(Stream destination, int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        return SaveToAsync(destination, null, 0, maxDegreeOfParallelism, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves the current stream to a buffer.
    /// </summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="progressCallback">The progress change callback. (optional)</param>
    /// <param name="userState">The user state for the <paramref name="progressCallback"/>. (optional)</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public async Task SaveBytesAsync(byte[] destination, Action<SaveProgressChangedEventArgs>? progressCallback,
        object? userState = null, int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, Length);

        var downloadState = new DownloadState<byte[]>(destination, _fileManifest, userState, progressCallback);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };
        
        if (_cacheAsIs)
            await Parallel.ForEachAsync(downloadState, parallelOptions, SaveAsIsAsync).ConfigureAwait(false);
        else
            await Parallel.ForEachAsync(downloadState, parallelOptions, SaveAsync).ConfigureAwait(false);

        return;

        static async ValueTask SaveAsync(ChunkWithOffset<byte[]> tuple, CancellationToken token)
        {
            await tuple.Chunk.ReadDataAsync(tuple.State.Destination, (int)tuple.Offset, (int)tuple.ChunkPartSize,
                (int)tuple.ChunkPartOffset, tuple.State.FileManifest.Manifest, token).ConfigureAwait(false);
            tuple.State.OnBytesWritten(tuple.ChunkPartSize);
        }

        static async ValueTask SaveAsIsAsync(ChunkWithOffset<byte[]> tuple, CancellationToken token)
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(tuple.State.FileManifest.Manifest.Options.ChunkDownloadBufferSize);

            try
            {
                await tuple.Chunk.ReadDataAsIsAsync(poolBuffer, tuple.State.FileManifest.Manifest, token).ConfigureAwait(false);
                Unsafe.CopyBlockUnaligned(ref tuple.State.Destination[tuple.Offset],
                    ref poolBuffer[tuple.ChunkPartOffset], tuple.ChunkPartSize);
                tuple.State.OnBytesWritten(tuple.ChunkPartSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }
    }

    /// <summary>
    /// Asynchronously saves the current stream to a buffer.
    /// </summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public Task SaveBytesAsync(byte[] destination, int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        return SaveBytesAsync(destination, null, 0, maxDegreeOfParallelism, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves the current stream to a buffer.
    /// </summary>
    /// <param name="progressCallback">The progress change callback. (optional)</param>
    /// <param name="userState">The user state for the <paramref name="progressCallback"/>. (optional)</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public async Task<byte[]> SaveBytesAsync(Action<SaveProgressChangedEventArgs> progressCallback, object? userState = null,
        int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        byte[] destination = new byte[Length];
        await SaveBytesAsync(destination, progressCallback, userState, maxDegreeOfParallelism, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    /// <summary>
    /// Asynchronously saves the current stream to a buffer.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public async Task<byte[]> SaveBytesAsync(int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        byte[] destination = new byte[Length];
        await SaveBytesAsync(destination, maxDegreeOfParallelism, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    /// <summary>
    /// Asynchronously saves the current stream to a file.
    /// </summary>
    /// <param name="path">The path of the destination file.</param>
    /// <param name="progressCallback">The progress change callback. (optional)</param>
    /// <param name="userState">The user state for the <paramref name="progressCallback"/>. (optional)</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent tasks saving/downloading to the destination. (optional)</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the entire save operation.</returns>
    public async Task SaveFileAsync(string path, Action<SaveProgressChangedEventArgs>? progressCallback, object? userState = null,
        int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        using SafeFileHandle destination = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.Asynchronous, Length);
        var downloadState = new DownloadState<SafeFileHandle>(destination, _fileManifest, userState, progressCallback);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };

        if (_cacheAsIs)
            await Parallel.ForEachAsync(downloadState, parallelOptions, SaveAsIsAsync).ConfigureAwait(false);
        else
            await Parallel.ForEachAsync(downloadState, parallelOptions, SaveAsync).ConfigureAwait(false);

        return;

        static async ValueTask SaveAsync(ChunkWithOffset<SafeFileHandle> tuple, CancellationToken token)
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent((int)tuple.ChunkPartSize);

            try
            {
                await tuple.Chunk.ReadDataAsync(poolBuffer, 0, (int)tuple.ChunkPartSize,
                    (int)tuple.ChunkPartOffset, tuple.State.FileManifest.Manifest, token).ConfigureAwait(false);
                await RandomAccess.WriteAsync(tuple.State.Destination,
                    new ReadOnlyMemory<byte>(poolBuffer, 0, (int)tuple.ChunkPartSize),
                    tuple.Offset, token).ConfigureAwait(false);
                tuple.State.OnBytesWritten(tuple.ChunkPartSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }

        static async ValueTask SaveAsIsAsync(ChunkWithOffset<SafeFileHandle> tuple, CancellationToken token)
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(tuple.State.FileManifest.Manifest.Options.ChunkDownloadBufferSize);

            try
            {
                await tuple.Chunk.ReadDataAsIsAsync(poolBuffer, tuple.State.FileManifest.Manifest, token).ConfigureAwait(false);
                await RandomAccess.WriteAsync(tuple.State.Destination,
                    new ReadOnlyMemory<byte>(poolBuffer, (int)tuple.ChunkPartOffset, (int)tuple.ChunkPartSize),
                    tuple.Offset, token).ConfigureAwait(false);
                tuple.State.OnBytesWritten(tuple.ChunkPartSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }
    }

    /// <inheritdoc cref="SaveFileAsync(string,Action{SaveProgressChangedEventArgs}?,object?,int?,CancellationToken)"/>
    public Task SaveFileAsync(string path, int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        return SaveFileAsync(path, null, 0, maxDegreeOfParallelism, cancellationToken);
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream.
    /// </summary>
    /// <param name="buffer">
    /// When this method returns, contains the specified byte array with the values between
    /// <paramref name="offset"></paramref> and (<paramref name="offset"></paramref> + <paramref name="count"></paramref> - 1)
    /// replaced by the characters read from the current stream.
    /// </param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing data from the current stream.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The total number of bytes written into the <paramref name="buffer"/>.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the the current stream.
    /// </summary>
    /// <param name="buffer">
    /// When this method returns, contains the specified byte array with the values between
    /// <paramref name="offset"></paramref> and (<paramref name="offset"></paramref> + <paramref name="count"></paramref> - 1)
    /// replaced by the characters read from the current stream.
    /// </param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing data from the current stream.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The total number of bytes written into the <paramref name="buffer"/>.</returns>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return await Task.FromCanceled<int>(cancellationToken).ConfigureAwait(false);

        int bytesRead = await ReadAtAsync(_position, buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _position += bytesRead;
        return bytesRead;
    }

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the current stream.
    /// </summary>
    /// <param name="buffer">The region of memory to write the data into.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The total number of bytes written into the <paramref name="buffer"/>.</returns>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int bytesRead = await ReadAtAsync(_position, buffer, cancellationToken).ConfigureAwait(false);
        _position += bytesRead;
        return bytesRead;
    }

    /// <summary>
    /// Reads a sequence of bytes from the given <paramref name="position"/> of the current stream.
    /// </summary>
    /// <param name="position">The position to begin reading from.</param>
    /// <param name="buffer">
    /// When this method returns, contains the specified byte array with the values between
    /// <paramref name="offset"></paramref> and (<paramref name="offset"></paramref> + <paramref name="count"></paramref> - 1)
    /// replaced by the characters read from the current stream.
    /// </param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing data from the current stream.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The total number of bytes written into the <paramref name="buffer"/>.</returns>
    public override int ReadAt(long position, byte[] buffer, int offset, int count)
    {
        return ReadAtAsync(position, buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the given <paramref name="position"/> of the current stream.
    /// </summary>
    /// <param name="position">The position to begin reading from.</param>
    /// <param name="buffer">
    /// When this method returns, contains the specified byte array with the values between
    /// <paramref name="offset"></paramref> and (<paramref name="offset"></paramref> + <paramref name="count"></paramref> - 1)
    /// replaced by the characters read from the current stream.
    /// </param>
    /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing data from the current stream.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The total number of bytes written into the <paramref name="buffer"/>.</returns>
    public override async Task<int> ReadAtAsync(long position, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (!_fileManifest.TryFindChunkPart(position, out int chunkPartIndex, out uint chunkPartOffset))
            return 0;

        uint bytesRead = 0u;

        if (_cacheAsIs)
        {
            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(_fileManifest.Manifest.Options.ChunkDownloadBufferSize);

            try
            {
                while (chunkPartIndex < _fileManifest.ChunkPartsArray.Length)
                {
                    FChunkPart chunkPart = _fileManifest.ChunkPartsArray[chunkPartIndex];

                    await chunkPart.Chunk.ReadDataAsIsAsync(poolBuffer, _fileManifest.Manifest, cancellationToken).ConfigureAwait(false);

                    uint chunkOffset = chunkPart.Offset + chunkPartOffset;
                    uint chunkBytes = chunkPart.Size - chunkPartOffset;
                    uint bytesLeft = (uint)count - bytesRead;

                    if (bytesLeft <= chunkBytes)
                    {
                        Unsafe.CopyBlockUnaligned(ref buffer[bytesRead + offset], ref poolBuffer[chunkOffset], bytesLeft);
                        bytesRead += bytesLeft;
                        break;
                    }

                    Unsafe.CopyBlockUnaligned(ref buffer[bytesRead + offset], ref poolBuffer[chunkOffset], chunkBytes);
                    bytesRead += chunkBytes;
                    chunkPartOffset = 0;

                    ++chunkPartIndex;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }
        }
        else
        {
            while (chunkPartIndex < _fileManifest.ChunkPartsArray.Length)
            {
                FChunkPart chunkPart = _fileManifest.ChunkPartsArray[chunkPartIndex];

                int chunkOffset = (int)(chunkPart.Offset + chunkPartOffset);
                int chunkBytes = (int)(chunkPart.Size - chunkPartOffset);
                int bytesLeft = count - (int)bytesRead;

                if (bytesLeft <= chunkBytes)
                {
                    await chunkPart.Chunk.ReadDataAsync(buffer, (int)bytesRead + offset, bytesLeft, chunkOffset, _fileManifest.Manifest, cancellationToken).ConfigureAwait(false);
                    bytesRead += (uint)bytesLeft;
                    break;
                }

                await chunkPart.Chunk.ReadDataAsync(buffer, (int)bytesRead + offset, chunkBytes, chunkOffset, _fileManifest.Manifest, cancellationToken).ConfigureAwait(false);
                bytesRead += (uint)chunkBytes;
                chunkPartOffset = 0;

                ++chunkPartIndex;
            }
        }

        return (int)bytesRead;
    }

    /// <summary>Sets the position within the current stream to the specified value.</summary>
    /// <param name="offset">The new position within the stream. This is relative to the <paramref name="loc"/> parameter, and can be positive or negative.</param>
    /// <param name="loc">A value of type <see cref="SeekOrigin"/>, which acts as the seek reference point.</param>
    /// <returns>The new position within the stream, calculated by combining the initial reference point and the offset.</returns>
    /// <exception cref="ArgumentException">There is an invalid <see cref="SeekOrigin"/>.</exception>
    public override long Seek(long offset, SeekOrigin loc)
    {
        Position = loc switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentException("Invalid loc", nameof(loc))
        };
        return _position;
    }
    
    /// <summary>Not supported</summary>
    public override void SetLength(long value)
        => throw new NotSupportedException();
    /// <summary>Not supported</summary>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
    /// <summary>Not supported</summary>
    public override void Flush()
        => throw new NotSupportedException();
}


/// <summary>
/// Provides data for the save progress changed event.
/// </summary>
public sealed class SaveProgressChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets an optional user-defined object that qualifies or contains
    /// information about the save operation.
    /// </summary>
    public object? UserState { get; }

    /// <summary>
    /// Gets the number of bytes written during this specific event.
    /// </summary>
    public long BytesWritten { get; }

    /// <summary>
    /// Gets the cumulative number of bytes written so far.
    /// </summary>
    public long TotalBytesWritten { get; }

    /// <summary>
    /// Gets the total number of bytes that will be written.
    /// </summary>
    public long TotalBytesToWrite { get; }

    /// <summary>
    /// Gets the progress of the save operation as a percentage (0–100),
    /// calculated from <see cref="TotalBytesWritten"/> and <see cref="TotalBytesToWrite"/>.
    /// </summary>
    public int ProgressPercentage { get; }

    internal SaveProgressChangedEventArgs(
        object? userState,
        long bytesWritten,
        long totalBytesWritten,
        long totalBytesToWrite,
        int progressPercentage)
    {
        UserState = userState;
        BytesWritten = bytesWritten;
        TotalBytesWritten = totalBytesWritten;
        TotalBytesToWrite = totalBytesToWrite;
        ProgressPercentage = progressPercentage;
    }
}


internal sealed class DownloadState<TDestination> : IEnumerable<ChunkWithOffset<TDestination>>, IEnumerator<ChunkWithOffset<TDestination>>
    where TDestination : class
{
    public readonly TDestination Destination;
    public readonly FFileManifest FileManifest;

    private readonly Lock? _lock;
    private readonly object? _userState;
    private readonly Action<SaveProgressChangedEventArgs>? _callback;
    private readonly long _totalBytesToSave;
    private long _bytesSaved;
    private int _lastProgress;

    // IEnumerable & IEnumerator
    private long _offset;
    private int _chunkpartIndex;

    public DownloadState(TDestination destination, FFileManifest fileManifest, object? userState, Action<SaveProgressChangedEventArgs>? callback)
    {
        Reset();
        Destination = destination;
        FileManifest = fileManifest;

        if (callback is null)
            return;
        _lock = new Lock();
        _userState = userState;
        _callback = callback;
        _totalBytesToSave = fileManifest.FileSize;
    }

    public void OnBytesWritten(long amount)
    {
        if (_callback is null)
            return;

        lock (_lock!)
        {
            _bytesSaved += amount;
            int progress = (int)MathF.Truncate((float)_bytesSaved / _totalBytesToSave * 100f);
            if (progress == _lastProgress)
                return;
            _lastProgress = progress;
            var eventArgs = new SaveProgressChangedEventArgs(_userState, amount, _bytesSaved, _totalBytesToSave, progress);
            _callback(eventArgs);
        }
    }

    // IEnumerable

    public IEnumerator<ChunkWithOffset<TDestination>> GetEnumerator() => this;
    IEnumerator IEnumerable.GetEnumerator() => this;

    // IEnumerator

    public bool MoveNext()
    {
        _chunkpartIndex++;
        if (_chunkpartIndex >= FileManifest.ChunkPartsArray.Length)
            return false;

        FChunkPart chunkPart = FileManifest.ChunkPartsArray[_chunkpartIndex];
        _offset = chunkPart.FileOffset;
        return true;
    }

    public void Reset()
    {
        _offset = 0;
        _chunkpartIndex = -1;
    }

    public ChunkWithOffset<TDestination> Current
    {
        get
        {
            FChunkPart chunkPart = FileManifest.ChunkPartsArray[_chunkpartIndex];
            return new ChunkWithOffset<TDestination>(this, chunkPart.Chunk, chunkPart.Offset, chunkPart.Size, _offset);
        }
    }

    object IEnumerator.Current => Current;

    public void Dispose() { }
}

internal readonly struct ChunkWithOffset<TDestination> where TDestination : class
{
    public readonly DownloadState<TDestination> State;
    public readonly FChunkInfo Chunk;
    public readonly uint ChunkPartOffset;
    public readonly uint ChunkPartSize;
    public readonly long Offset;

    public ChunkWithOffset(DownloadState<TDestination> state, FChunkInfo chunk, uint chunkPartOffset, uint chunkPartSize, long offset)
    {
        State = state;
        Chunk = chunk;
        ChunkPartOffset = chunkPartOffset;
        ChunkPartSize = chunkPartSize;
        Offset = offset;
    }
}
