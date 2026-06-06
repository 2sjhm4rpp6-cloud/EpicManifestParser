using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Microsoft.Win32.SafeHandles;

using OffiUtils;

namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ChunkData.h?plain=1#L263C1-L266C19
/// <summary>
/// UE FChunkInfo struct
/// </summary>
public sealed class FChunkInfo
{
    /// <summary>
    /// The GUID for this data.
    /// </summary>
    public FGuid Guid { get; internal set; }
    /// <summary>
    /// The FRollingHash hashed value for this chunk data.
    /// </summary>
    public uint64 Hash { get; internal set; }
    /// <summary>
    /// The FSHA hashed value for this chunk data.
    /// </summary>
    public FSHAHash ShaHash { get; internal set; }
    /// <summary>
    /// The group number this chunk divides into.
    /// </summary>
    public uint8 GroupNumber { get; internal set; }
    /// <summary>
    /// The size of this data compressed.
    /// </summary>
    public uint32 DataSizeCompressed { get; internal set; }
    /// <summary>
    /// The size of this data uncompressed (also known as window size in older algorithms).
    /// </summary>
    public uint32 DataSizeUncompressed { get; internal set; }
    /// <summary>
    /// The file download size for this chunk.
    /// </summary>
    public int64 FileSize { get; internal set; }

    /// <summary>
    /// The ID of the encryption secret key used by this chunk.
    /// </summary>
    internal FGuid? EncryptionSecretId { get; set; }
    /// <summary>
    /// The 16 byte AuthTag space used for AES encryption, which is an additional integrity check value.
    /// </summary>
    internal FAESAuthTag? AESAuthTag { get; set; }

    /// <param name="manifest"></param>
    /// <returns>
    /// Url <see cref="string"/> to download this chunk
    /// </returns>
    public string GetUrl(FBuildPatchAppManifest manifest) =>
        $"{manifest.Options.ChunkBaseUrl}{manifest.Meta.ChunkSubdir}/{GroupNumber:D2}/{Hash:X16}_{Guid}.chunk";

    /// <param name="manifest"></param>
    /// <returns>
    /// <see cref="Uri"/> to download this chunk
    /// </returns>
    public Uri GetUri(FBuildPatchAppManifest manifest) => new(GetUrl(manifest), UriKind.Absolute);

    internal string? CachePath { get; set; }

    // https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L579C67-L579C67
    internal static FChunkInfo[] ReadChunkDataList(ref ManifestReader reader, Dictionary<FGuid, FChunkInfo> chunksDict)
    {
        int startPos = reader.Position;
        int32 dataSize = reader.Read<int32>();
        EChunkDataListVersion dataVersion = reader.Read<EChunkDataListVersion>();
        int32 elementCount = reader.Read<int32>();

        var chunks = new FChunkInfo[elementCount];
        Span<FChunkInfo> chunksSpan = chunks.AsSpan();

        chunksDict.EnsureCapacity(elementCount);

        if (dataVersion >= EChunkDataListVersion.Original)
        {
            for (int i = 0; i < elementCount; i++)
            {
                var chunk = new FChunkInfo();
                chunk.Guid = reader.Read<FGuid>();
                chunksSpan[i] = chunk;
                chunksDict.Add(chunk.Guid, chunk);
            }
            for (int i = 0; i < elementCount; i++)
                chunksSpan[i].Hash = reader.Read<uint64>();
            for (int i = 0; i < elementCount; i++)
                chunksSpan[i].ShaHash = reader.Read<FSHAHash>();
            for (int i = 0; i < elementCount; i++)
                chunksSpan[i].GroupNumber = reader.Read<uint8>();
            for (int i = 0; i < elementCount; i++)
                chunksSpan[i].DataSizeUncompressed = reader.Read<uint32>();
            for (int i = 0; i < elementCount; i++)
                chunksSpan[i].FileSize = reader.Read<int64>();

            if (dataVersion >= EChunkDataListVersion.SerialisesEncryptionSecretId)
            {
                for (int i = 0; i < elementCount; i++)
                    chunksSpan[i].EncryptionSecretId = reader.Read<FGuid>();
            }

            if (dataVersion >= EChunkDataListVersion.SerialisesCompressesDataSize)
            {
                for (int i = 0; i < elementCount; i++)
                    chunksSpan[i].DataSizeCompressed = reader.Read<uint32>();
            }

            if (dataVersion >= EChunkDataListVersion.SerialisesAESAuthTag)
            {
                for (int i = 0; i < elementCount; i++)
                    chunksSpan[i].AESAuthTag = reader.Read<FAESAuthTag>();
            }
        }
        else
        {
            var defaultChunk = new FChunkInfo
            {
                DataSizeUncompressed = 1048576
            };
            chunksSpan.Fill(defaultChunk);
        }

        reader.Position = startPos + dataSize;
        return chunks;
    }

    [SuppressMessage("ReSharper", "UseSymbolAlias")]
    internal async Task<int> ReadDataAsIsAsync(byte[] destination, FBuildPatchAppManifest manifest, CancellationToken cancellationToken = default)
    {
        int fileSize = 0;
        bool shouldCache = manifest.Options.ChunkCacheDirectory is not null;
        string? cachePath = null;

        if (CachePath is not null)
        {
            using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
            fileSize = (int)RandomAccess.GetLength(fileHandle);
            await RandomAccess.ReadAsync(fileHandle, destination.AsMemory(0, fileSize), 0, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using IDisposable _ = await manifest.ChunksLocker.LockAsync(Guid, cancellationToken).ConfigureAwait(false);

            if (CachePath is not null)
            {
                using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
                fileSize = (int)RandomAccess.GetLength(fileHandle);
                await RandomAccess.ReadAsync(fileHandle, destination.AsMemory(0, fileSize), 0, cancellationToken).ConfigureAwait(false);
            }
            else if (shouldCache)
            {
                cachePath = GetCachePath(manifest, true);
                if (File.Exists(cachePath))
                {
                    CachePath = cachePath;
                    using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
                    fileSize = (int)RandomAccess.GetLength(fileHandle);
                    await RandomAccess.ReadAsync(fileHandle, destination.AsMemory(0, fileSize), 0, cancellationToken).ConfigureAwait(false);
                }
            }

            if (fileSize == 0)
            {
                Uri uri = GetUri(manifest);
                var destMs = new MemoryStream(destination, 0, destination.Length, true);
                using HttpResponseMessage res = await manifest.Options.Client!.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                EnsureSuccessStatusCode(res, uri);
                await res.Content.CopyToAsync(destMs, cancellationToken).ConfigureAwait(false);
                fileSize = (int)destMs.Position;

                if (shouldCache)
                {
                    using (SafeFileHandle fileHandle = File.OpenHandle(cachePath!, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, fileSize))
                    {
                        await RandomAccess.WriteAsync(fileHandle, new ReadOnlyMemory<byte>(destination, 0, fileSize), 0, cancellationToken).ConfigureAwait(false);
                    }
                    CachePath = cachePath;
                }
            }
        }

        var header = FChunkHeader.Parse(new ManifestData(destination, 0, fileSize));

        if (header.StoredAs.HasFlag(EChunkStorageFlags.Encrypted))
            throw new NotSupportedException("Encrypted chunks are not supported");

        if (header.StoredAs == EChunkStorageFlags.None)
        {
            Unsafe.CopyBlockUnaligned(ref destination[0], ref destination[header.HeaderSize], (uint)header.DataSizeCompressed);
            return header.DataSizeCompressed;
        }

        if (header.StoredAs != EChunkStorageFlags.Compressed)
            throw new UnreachableException("Unknown/new chunk ChunkStorageFlag");
        manifest.Options.Decompressor ??= DecompressorBuilder.Default.Build();

        // cant uncompress in-place
        byte[] poolBuffer = ArrayPool<byte>.Shared.Rent(header.DataSizeCompressed);

        try
        {
            Unsafe.CopyBlockUnaligned(ref poolBuffer[0], ref destination[header.HeaderSize], (uint)header.DataSizeCompressed);

            if (!manifest.Options.Decompressor.TryDecompress(CompressionAlgorithm.Zlib,
                new ReadOnlySpan<byte>(poolBuffer, 0, header.DataSizeCompressed),
                new Span<byte>(destination, 0, header.DataSizeUncompressed),
                out int bytesWritten) || bytesWritten != header.DataSizeUncompressed)
            {
                throw new FileLoadException("Failed to uncompress data");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
        }

        return header.DataSizeUncompressed;
    }

    [SuppressMessage("ReSharper", "UseSymbolAlias")]
    internal async Task<int> ReadDataAsync(byte[] buffer, int offset, int count, int chunkPartOffset, FBuildPatchAppManifest manifest, CancellationToken cancellationToken = default)
    {
        if (CachePath is not null)
        {
            using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
            return await RandomAccess.ReadAsync(fileHandle, buffer.AsMemory(offset, count), chunkPartOffset, cancellationToken).ConfigureAwait(false);
        }

        using IDisposable _ = await manifest.ChunksLocker.LockAsync(Guid, cancellationToken).ConfigureAwait(false);

        if (CachePath is not null)
        {
            using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
            return await RandomAccess.ReadAsync(fileHandle, buffer.AsMemory(offset, count), chunkPartOffset, cancellationToken).ConfigureAwait(false);
        }

        bool shouldCache = manifest.Options.ChunkCacheDirectory is not null;
        string? cachePath = null;

        if (shouldCache)
        {
            cachePath = GetCachePath(manifest, false);
            if (File.Exists(cachePath))
            {
                CachePath = cachePath;
                using SafeFileHandle fileHandle = File.OpenHandle(CachePath);
                return await RandomAccess.ReadAsync(fileHandle, buffer.AsMemory(offset, count), chunkPartOffset, cancellationToken).ConfigureAwait(false);
            }
        }

        byte[]? poolBuffer = null;
        byte[]? uncompressPoolBuffer = null;

        try
        {
            Uri uri = GetUri(manifest);
            using HttpResponseMessage res = await manifest.Options.Client!.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            EnsureSuccessStatusCode(res, uri);
            long poolBufferSize = res.Content.Headers.ContentLength ?? manifest.Options.ChunkDownloadBufferSize;
            poolBuffer = ArrayPool<byte>.Shared.Rent((int)poolBufferSize);
            var destMs = new MemoryStream(poolBuffer, 0, poolBuffer.Length, true, true);
            await res.Content.CopyToAsync(destMs, cancellationToken).ConfigureAwait(false);
            int responseSize = (int)destMs.Position;

            var header = FChunkHeader.Parse(new ManifestData(poolBuffer, 0, responseSize));

            if (header.StoredAs.HasFlag(EChunkStorageFlags.Encrypted))
                throw new NotSupportedException("Encrypted chunks are not supported");

            if (header.StoredAs == EChunkStorageFlags.None)
            {
                Unsafe.CopyBlockUnaligned(ref buffer[offset], ref poolBuffer[header.HeaderSize + chunkPartOffset], (uint)count);
                if (!shouldCache)
                    return count;
                using (SafeFileHandle fileHandle = File.OpenHandle(cachePath!, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, header.DataSizeCompressed))
                {
                    await RandomAccess.WriteAsync(fileHandle, new ReadOnlyMemory<byte>(poolBuffer, header.HeaderSize, header.DataSizeCompressed), 0, cancellationToken).ConfigureAwait(false);
                }
                CachePath = cachePath;
                return count;
            }

            if (header.StoredAs != EChunkStorageFlags.Compressed)
                throw new UnreachableException("Unknown/new chunk ChunkStorageFlag");
            if (manifest.Options.Decompressor is null)
                throw new InvalidOperationException("Data is compressed and decompressor delegate was null");

            // cant seek for uncompression
            uncompressPoolBuffer = ArrayPool<byte>.Shared.Rent(header.DataSizeUncompressed);

            if (!manifest.Options.Decompressor.TryDecompress(CompressionAlgorithm.Zlib,
                new ReadOnlySpan<byte>(poolBuffer, header.HeaderSize, header.DataSizeCompressed),
                new Span<byte>(uncompressPoolBuffer, 0, header.DataSizeUncompressed),
                out int bytesWritten) || bytesWritten != header.DataSizeUncompressed)
            {
                throw new FileLoadException("Failed to uncompress data");
            }

            Unsafe.CopyBlockUnaligned(ref buffer[offset], ref uncompressPoolBuffer[chunkPartOffset], (uint)count);
            if (!shouldCache)
                return count;
            using (SafeFileHandle fileHandle = File.OpenHandle(cachePath!, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None, header.DataSizeUncompressed))
            {
                await RandomAccess.WriteAsync(fileHandle, new ReadOnlyMemory<byte>(uncompressPoolBuffer, 0, header.DataSizeUncompressed), 0, cancellationToken).ConfigureAwait(false);
            }
            CachePath = cachePath;
            return count;
        }
        finally
        {
            if (poolBuffer is not null)
                ArrayPool<byte>.Shared.Return(poolBuffer);
            if (uncompressPoolBuffer is not null)
                ArrayPool<byte>.Shared.Return(uncompressPoolBuffer);
        }
    }

    // Format: {Hash:X16}_{Guid}.chunk
    // "v2_": 3, Hash: 16 chars, '_': 1, CustomGuid: 32, ".chunk": 6 => total: 58 chars
    private const int32 FileNameLength = 3 + 16 + 1 + 32 + 6;

    private string GetCachePath(FBuildPatchAppManifest manifest, bool v2)
    {
        Span<char> fileName = stackalloc char[FileNameLength];
        if (!TryWriteChunkFileName(v2, Hash, Guid, fileName, out int32 charsWritten))
            throw new Exception("Failed to create chunk fileName");

        return Path.Join(manifest.Options.ChunkCacheDirectory, fileName[..charsWritten]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryWriteChunkFileName(
        bool v2,
        uint64 hash,
        FGuid guid,
        Span<char> destination,
        out int32 charsWritten)
    {
        charsWritten = 0;

        if (destination.Length < FileNameLength)
            return false;

        if (v2)
        {
            "v2_".CopyTo(destination);
            charsWritten = 3;
        }

        if (!hash.TryFormat(destination[charsWritten..], out int hashLen, "X16"))
            return false;
        charsWritten += hashLen;

        destination[charsWritten++] = '_';

        if (!guid.TryFormat(destination[charsWritten..], out int guidLen, default))
            return false;
        charsWritten += guidLen;

        ".chunk".CopyTo(destination[charsWritten..]);
        charsWritten += 6;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void EnsureSuccessStatusCode(HttpResponseMessage res, Uri uri)
    {
        try
        {
            res.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            ex.Data.Add("Uri", uri);
            ex.Data.Add("Headers", res.Headers);
            throw;
        }
    }

    // ReSharper disable once UseSymbolAlias
    internal static void TestDecompress(byte[] uncompressPoolBuffer, byte[] chunkBuffer, IDecompressor decompressor)
    {
        var header = FChunkHeader.Parse(chunkBuffer);

        if (!decompressor.TryDecompress(CompressionAlgorithm.Zlib,
            new ReadOnlySpan<byte>(chunkBuffer, header.HeaderSize, header.DataSizeCompressed),
            new Span<byte>(uncompressPoolBuffer, 0, header.DataSizeUncompressed),
            out int bytesWritten) || bytesWritten != header.DataSizeUncompressed)
        {
            throw new FileLoadException("Failed to uncompress data");
        }
    }
}
