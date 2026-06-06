using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

using AsyncKeyedLock;

using CommunityToolkit.HighPerformance.Buffers;

using EpicManifestParser.Json;

using Microsoft.Win32.SafeHandles;

using OffiUtils;

namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/BuildPatchManifest.h?plain=1#L60C1-L63C29
/// <summary>
/// UE FBuildPatchAppManifest struct
/// </summary>
public class FBuildPatchAppManifest
{
    /// <summary/>
    public FManifestMeta Meta { get; internal set; } = null!;
    /// <summary/>
    public IReadOnlyList<FChunkInfo> ChunkList { get; internal set; } = null!;
    /// <summary/>
    public IReadOnlyList<FFileManifest> Files { get; internal set; } = null!;
    /// <summary/>
    public IReadOnlyList<FCustomField> CustomFields { get; internal set; } = null!;
    /// <summary/>
    public IReadOnlyDictionary<FGuid, FChunkInfo> Chunks { get; internal set; } = null!;

    /// <summary/>
    public int64 TotalBuildSize { get; internal set; }
    /// <summary/>
    public int64 TotalDownloadSize { get; internal set; }

    // The encryption secret ID and hash used for this manifest, if encrypted.
    // These are serialised with the manifest header, not the main data.
    internal FGuid? EncryptionSecretId { get; set; }
    internal FAESAuthTag? EncryptionAuthTag { get; set; }
    internal FEncryptedData? EncryptedData { get; set; }

    internal ManifestParseOptions Options { get; init; } = null!;
    internal AsyncKeyedLocker<FGuid> ChunksLocker { get; set; } = null!;

    internal FBuildPatchAppManifest() { }

    /// <summary>
    /// Finds a file by <see cref="FFileManifest.FileName"/>.
    /// </summary>
    /// <param name="fileName">The filename to find.</param>
    /// <param name="comparisonType">The type to compare the filename.</param>
    /// <returns>The <see cref="FFileManifest"/> instance if the the file was found; otherwise, <see langword="null"/>.</returns>
    public FFileManifest? FindFile(string fileName, StringComparison comparisonType = StringComparison.Ordinal)
        => TryFindFile(fileName, comparisonType, out FFileManifest? file) ? file : null;

    /// <summary>
    /// Tries to find a file by <see cref="FFileManifest.FileName"/> using <see cref="StringComparison.Ordinal"/> to compare it.
    /// </summary>
    /// <param name="fileName">The filename to find.</param>
    /// <param name="fileManifest">The find result.</param>
    /// <returns><see langword="true"/> if the the file was found; otherwise, <see langword="false"/>.</returns>
    public bool TryFindFile(string fileName, [NotNullWhen(true)] out FFileManifest? fileManifest)
        => TryFindFile(fileName, StringComparison.Ordinal, out fileManifest);

    /// <summary>
    /// Tries to find a file by <see cref="FFileManifest.FileName"/>.
    /// </summary>
    /// <param name="fileName">The filename to find.</param>
    /// <param name="comparisonType">The type to compare the filename.</param>
    /// <param name="fileManifest">The find result.</param>
    /// <returns><see langword="true"/> if the the file was found; otherwise, <see langword="false"/>.</returns>
    public bool TryFindFile(string fileName, StringComparison comparisonType, [NotNullWhen(true)] out FFileManifest? fileManifest)
    {
        foreach (FFileManifest file in Files)
        {
            if (!file.FileName.Equals(fileName, comparisonType))
                continue;

            fileManifest = file;
            return true;
        }

        fileManifest = null;
        return false;
    }

    /// <summary>
    /// Helper function to decide whether the passed in data is a JSON string we expect to deserialize a manifest from
    /// </summary>
    /// <returns><see langword="true"/> if the <paramref name="dataInput"/> is JSON; otherwise, <see langword="false"/>.</returns>
    public static bool IsJson(ManifestRoData dataInput)
    {
        // The best we can do is look for the mandatory first character open curly brace,
        // it will be within the first 4 characters (may have BOM)
        return dataInput[..4].Contains((byte)'{');
    }

    /// <summary>
    /// Deserializes a binary or JSON manifest
    /// </summary>
    /// <inheritdoc cref="DeserializeBinary(ManifestRoData,Action{ManifestParseOptions}?)"/>
    public static FBuildPatchAppManifest Deserialize(ManifestRoData dataInput, Action<ManifestParseOptions>? optionsBuilder = null)
    {
        var options = new ManifestParseOptions();
        optionsBuilder?.Invoke(options);
        return Deserialize(dataInput, options);
    }

    /// <summary>
    /// Deserializes a JSON manifest
    /// </summary>
    /// <param name="dataInput">The span to parse from</param>
    /// <param name="optionsBuilder">Builder for options/configuration to parse</param>
    public static FBuildPatchAppManifest DeserializeJson(ManifestRoData dataInput, Action<ManifestParseOptions>? optionsBuilder = null)
    {
        var options = new ManifestParseOptions();
        optionsBuilder?.Invoke(options);
        return DeserializeJson(dataInput, options);
    }

    /// <summary>
    /// Deserializes a binary manifest
    /// </summary>
    /// <param name="dataInput">The span to parse from</param>
    /// <param name="optionsBuilder">Builder for options/configuration to parse</param>
    /// <exception cref="NotSupportedException">Manifest is older than <see cref="EFeatureLevel.StoredAsBinaryData"/></exception>
    /// <exception cref="FileLoadException">Error while parsing or decompression</exception>
    /// <exception cref="InvalidDataException">Hashes do not match</exception>
    public static FBuildPatchAppManifest DeserializeBinary(ManifestRoData dataInput, Action<ManifestParseOptions>? optionsBuilder = null)
    {
        var options = new ManifestParseOptions();
        optionsBuilder?.Invoke(options);
        return DeserializeBinary(dataInput, options);
    }

    /// <summary>
    /// Deserializes a binary or JSON manifest
    /// </summary>
    /// <inheritdoc cref="DeserializeBinary(ManifestRoData,ManifestParseOptions)"/>
    public static FBuildPatchAppManifest Deserialize(ManifestRoData dataInput, ManifestParseOptions options)
    {
        return IsJson(dataInput)
            ? DeserializeJson(dataInput, options)
            : DeserializeBinary(dataInput, options);
    }

    /// <summary>
    /// Deserializes a binary or JSON manifest
    /// </summary>
    /// <param name="path">The file path to parse from</param>
    /// <param name="options">Options/Configuration to parse</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <inheritdoc cref="DeserializeBinary(ManifestRoData,ManifestParseOptions)"/>
    public static async ValueTask<FBuildPatchAppManifest> DeserializeFileAsync(
        string path, ManifestParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        using SafeFileHandle handle = File.OpenHandle(path);
        int size = (int)RandomAccess.GetLength(handle);
        using var dataInputOwner = MemoryOwner<byte>.Allocate(size);
        Memory<byte> dataInputMemory = dataInputOwner.Memory;
        await RandomAccess.ReadAsync(handle, dataInputMemory, 0, cancellationToken).ConfigureAwait(false);

        options ??= new ManifestParseOptions();
        ManifestData dataInput = dataInputMemory.Span;
        return Deserialize(dataInput, options);
    }

    /// <summary>
    /// Deserializes a JSON manifest
    /// </summary>
    /// <param name="dataInput">The span to parse from</param>
    /// <param name="options">Options/Configuration to parse</param>
    public static FBuildPatchAppManifest DeserializeJson(ManifestRoData dataInput, ManifestParseOptions? options = null)
    {
        JsonObject reader = JsonNode.Parse(dataInput)!.AsObject();

        EFeatureLevel featureLevel = reader["ManifestFileVersion"].GetBlob(EFeatureLevel.CustomFields);
        if (featureLevel == EFeatureLevel.BrokenJsonVersion)
            featureLevel = EFeatureLevel.StoresChunkFileSizes;

        var meta = new FManifestMeta
        {
            FeatureLevel = featureLevel,
            ChunkSubdir = FManifestMeta.GetChunkSubdir(featureLevel),
            AppID = reader["AppID"].GetBlob<uint32>(),
            AppName = reader["AppNameString"].GetString(),
            BuildVersion = reader["BuildVersionString"].GetString(),
            LaunchExe = reader["LaunchExeString"].GetString(),
            LaunchCommand = reader["LaunchCommand"].GetString(),
            PrereqName = reader["PrereqName"].GetString(),
            PrereqPath = reader["PrereqPath"].GetString(),
            PrereqArgs = reader["PrereqArgs"].GetString(),
            UninstallActionPath = "",
            UninstallActionArgs = ""
        };

        JsonArray jsonFileManifestList = reader["FileManifestList"]!.AsArray();
        var fileManifests = new FFileManifest[jsonFileManifestList.Count];
        Span<FFileManifest> fileManifestsSpan = fileManifests.AsSpan();

        //var allDataGuids = new HashSet<FGuid>();
        var mutableChunkInfoLookup = new Dictionary<FGuid, FChunkInfo>();

        for (int i = 0; i < fileManifestsSpan.Length; i++)
        {
            JsonNode jsonFileManifest = jsonFileManifestList[i]!;
            FFileManifest fileManifest = fileManifestsSpan[i] = new FFileManifest
            {
                FileName = jsonFileManifest["Filename"].GetString(),
                SHA1Hash = jsonFileManifest["FileHash"].GetBlob<FSHAHash>(),
                InstallTags = jsonFileManifest["InstallTags"].Parse<string[]>([]),
                SymlinkTarget = jsonFileManifest["SymlinkTarget"].GetString()
            };
            JsonArray jsonFileChunkParts = jsonFileManifest["FileChunkParts"]!.AsArray();
            fileManifest.ChunkPartsArray = new FChunkPart[jsonFileChunkParts.Count];
            Span<FChunkPart> chunkPartsSpan = fileManifest.ChunkPartsArray.AsSpan();
            long chunkPartsFileOffset = 0L;
            for (int j = 0; j < chunkPartsSpan.Length; j++)
            {
                JsonNode jsonFileChunkPart = jsonFileChunkParts[j]!;
                FGuid chunkPartGuid = jsonFileChunkPart["Guid"].GetFGuid();
                uint32 chunkPartOffset = jsonFileChunkPart["Offset"].GetBlob<uint32>();
                uint32 chunkPartSize = jsonFileChunkPart["Size"].GetBlob<uint32>();
                chunkPartsSpan[j] = new FChunkPart(chunkPartGuid, chunkPartOffset, chunkPartSize, chunkPartsFileOffset, mutableChunkInfoLookup);
                chunkPartsFileOffset += chunkPartSize;
            }

            if (jsonFileManifest["bIsUnixExecutable"].Get<bool>())
                fileManifest.FileMetaFlags |= EFileMetaFlags.UnixExecutable;
            if (jsonFileManifest["bIsReadOnly"].Get<bool>())
                fileManifest.FileMetaFlags |= EFileMetaFlags.ReadOnly;
            if (jsonFileManifest["bIsCompressed"].Get<bool>())
                fileManifest.FileMetaFlags |= EFileMetaFlags.Compressed;
        }

        var chunkList = new FChunkInfo[mutableChunkInfoLookup.Count];
        Span<FChunkInfo> chunkListSpan = chunkList.AsSpan();
        int chunkIndex = 0;
        foreach (FChunkInfo chunk in mutableChunkInfoLookup.Values)
        {
            chunkListSpan[chunkIndex++] = chunk;
        }

        bool hasChunkHashList = false;
        JsonNode? jsonChunkHashListNode = reader["ChunkHashList"];
        if (jsonChunkHashListNode is not null)
        {
            JsonObject jsonChunkHashList = jsonChunkHashListNode.AsObject();

            foreach ((string guidString, JsonNode? jsonChunkHash) in jsonChunkHashList)
            {
                var guid = new FGuid(guidString);
                uint64 chunkHash = jsonChunkHash.GetBlob<uint64>();
                mutableChunkInfoLookup[guid].Hash = chunkHash;
            }

            hasChunkHashList = true;
        }

        JsonNode? jsonChunkShaListNode = reader["ChunkShaList"];
        if (jsonChunkShaListNode is not null)
        {
            JsonObject jsonChunkShaList = jsonChunkShaListNode.AsObject();

            foreach ((string guidString, JsonNode? jsonSha) in jsonChunkShaList)
            {
                var guid = new FGuid(guidString);
                FSHAHash chunkSha = jsonSha.GetSha();
                mutableChunkInfoLookup[guid].ShaHash = chunkSha;
            }
        }

        string[]? prereqIds = reader["PrereqIds"].Deserialize<string[]>();
        if (prereqIds is null)
        {
            // TODO: https://github.com/EpicGames/UnrealEngine/blob/8c31706601135aadf2f957fb76e2af46f04a8ef9/Engine/Source/Runtime/Online/BuildPatchServices/Private/BuildPatchManifest.cpp#L602
            meta.PrereqIds = [];
        }
        else
        {
            meta.PrereqIds = prereqIds;
        }

        JsonNode? jsonDataGroupListNode = reader["DataGroupList"];
        if (jsonDataGroupListNode is not null)
        {
            JsonObject jsonDataGroupList = jsonDataGroupListNode.AsObject();

            foreach ((string guidString, JsonNode? jsonDataGroup) in jsonDataGroupList)
            {
                var guid = new FGuid(guidString);
                uint8 dataGroup = jsonDataGroup.GetBlob<uint8>();
                mutableChunkInfoLookup[guid].GroupNumber = dataGroup;
            }
        }
        else
        {
            // TODO: https://github.com/EpicGames/UnrealEngine/blob/8c31706601135aadf2f957fb76e2af46f04a8ef9/Engine/Source/Runtime/Online/BuildPatchServices/Private/BuildPatchManifest.cpp#L635
            //       https://github.com/EpicGames/UnrealEngine/blob/8c31706601135aadf2f957fb76e2af46f04a8ef9/Engine/Source/Runtime/Core/Private/Misc/Crc.cpp#L592
        }

        bool hasChunkFilesizeList = false;
        JsonNode? jsonChunkFilesizeListNode = reader["ChunkFilesizeList"];
        if (jsonChunkFilesizeListNode is not null)
        {
            JsonObject jsonChunkFilesizeList = jsonChunkFilesizeListNode.AsObject();

            foreach ((string guidString, JsonNode? jsonFileSize) in jsonChunkFilesizeList)
            {
                var guid = new FGuid(guidString);
                int64 fileSize = jsonFileSize.GetBlob<int64>();
                mutableChunkInfoLookup[guid].FileSize = fileSize;
            }

            hasChunkFilesizeList = true;
        }

        if (!hasChunkFilesizeList)
        {
            // Missing chunk list, version before we saved them compressed. Assume original fixed chunk size of 1 MiB.
            foreach (FChunkInfo chunk in chunkListSpan)
            {
                chunk.FileSize = 1048576;
            }
        }

        if (reader.TryGetPropertyValue("bIsFileData", out JsonNode? jsonIsFileData))
        {
            meta.bIsFileData = jsonIsFileData.Get<bool>();
        }
        else
        {
            meta.bIsFileData = !hasChunkHashList;
        }

        FCustomField[]? customFields = null;
        JsonNode? jsonCustomFieldsNode = reader["CustomFields"];
        if (jsonCustomFieldsNode is not null)
        {
            JsonObject jsonCustomFields = jsonCustomFieldsNode.AsObject();
            customFields = new FCustomField[jsonCustomFields.Count];
            int customFieldIndex = 0;

            foreach ((string name, JsonNode? jsonValue) in jsonCustomFields)
            {
                customFields[customFieldIndex++] = new FCustomField
                {
                    Name = name,
                    Value = jsonValue.GetString()
                };
            }
        }

        meta.BuildId = FManifestMeta.GetBackwardsCompatibleBuildId(meta);

        var manifest = new FBuildPatchAppManifest
        {
            Meta = meta,
            ChunkList = chunkList,
            Files = fileManifests,
            CustomFields = customFields ?? [],
            Chunks = mutableChunkInfoLookup,
            Options = options ?? new ManifestParseOptions()
        };
        manifest.PostSetup();

        // FileDataList.OnPostLoad();
        {
            Array.Sort(fileManifests);
            for (int i = 0; i < fileManifestsSpan.Length; i++)
            {
                FFileManifest file = fileManifestsSpan[i];
                file.Manifest = manifest;
                foreach (FChunkPart chunkPart in file.ChunkPartsArray.AsSpan())
                {
                    file.FileSize += chunkPart.Size;
                }
            }
        }

        return manifest;
    }

    /// <summary>
    /// Deserializes a binary manifest
    /// </summary>
    /// <param name="dataInput">The span to parse from</param>
    /// <param name="options">Options/Configuration to parse</param>
    /// <exception cref="NotSupportedException">Manifest is older than <see cref="EFeatureLevel.StoredAsBinaryData"/></exception>
    /// <exception cref="FileLoadException">Error while parsing or decompression</exception>
    /// <exception cref="InvalidDataException">Hashes do not match</exception>
    public static FBuildPatchAppManifest DeserializeBinary(ManifestRoData dataInput, ManifestParseOptions? options = null)
    {
        var fileReader = new ManifestReader(dataInput);
        var header = new FManifestHeader(ref fileReader);

        if (header.Version < EFeatureLevel.StoredAsBinaryData)
            throw new NotSupportedException("Manifests below feature level StoredAsBinaryData are not supported");

        bool isCompressed = header.StoredAs.HasFlag(EManifestStorageFlags.Compressed);
        bool isEncrypted = header.StoredAs.HasFlag(EManifestStorageFlags.Encrypted);
        EManifestStorageFlags storedAsRemaining = header.StoredAs & ~(EManifestStorageFlags.Compressed | EManifestStorageFlags.Encrypted);
        if (storedAsRemaining != EManifestStorageFlags.None)
            throw new UnreachableException("Manifest has invalid or unknown storage flags");

        options ??= new ManifestParseOptions();

        int32 bufferSize = isCompressed
            ? header.DataSizeUncompressed
            : header.DataSizeCompressed;

        using var buffer = SpanOwner<uint8>.Allocate(bufferSize);

        ManifestData manifestRawData;

        if (isCompressed)
        {
            options.Decompressor ??= DecompressorBuilder.Default.Build();

            ManifestData manifestCompressedData = fileReader.ReadSpan(header.DataSizeCompressed);
            manifestRawData = buffer.Span;

            if (!options.Decompressor.TryDecompress(
                    CompressionAlgorithm.Zlib,
                    manifestCompressedData,
                    manifestRawData,
                    out int bytesWritten) ||
                bytesWritten != header.DataSizeUncompressed)
            {
                throw new FileLoadException("Failed to uncompress data");
            }
        }
        else
        {
            manifestRawData = fileReader.ReadSpan(header.DataSizeCompressed);
        }

        var hash = FSHAHash.Compute(manifestRawData);
        if (header.SHAHash != hash)
            throw new InvalidDataException($"Hash does not match. expected: {header.SHAHash}, actual: {hash}");

        var reader = new ManifestReader(manifestRawData);
        var chunks = new Dictionary<FGuid, FChunkInfo>();
        var manifest = new FBuildPatchAppManifest
        {
            Chunks = chunks,
            Options = options
        };

        if (header.EncryptionSecretId.HasValue)
        {
            manifest.EncryptionSecretId = header.EncryptionSecretId;
            manifest.EncryptionAuthTag = header.EncryptionAuthTag;
        }

        manifest.Meta = new FManifestMeta(ref reader);
        manifest.ChunkList = FChunkInfo.ReadChunkDataList(ref reader, chunks);
        manifest.Files = FFileManifest.ReadFileDataList(ref reader, manifest);
        manifest.CustomFields = FCustomField.ReadCustomFields(ref reader);

        if (header.Version >= EFeatureLevel.ManifestEncryptionSupport)
        {
            if (header.EncryptionSecretId.HasValue && header.EncryptionSecretId.Value.IsValid())
            {
                manifest.EncryptedData = FEncryptedData.ReadEncryptedData(ref reader);
                // TODO: read "header"
            }
        }

        manifest.PostSetup();

        return manifest;
    }

    // TODO
    private void DecryptData()
    {

    }

    private void PostSetup()
    {
        foreach (FFileManifest file in Files)
        {
            TotalBuildSize += file.FileSize;
        }

        foreach (FChunkInfo chunk in ChunkList)
        {
            TotalDownloadSize += chunk.FileSize;
        }

        if (!string.IsNullOrEmpty(Options.ChunkBaseUrl))
        {
            ChunksLocker = new AsyncKeyedLocker<FGuid>(lockerOptions =>
            {
                lockerOptions.MaxCount = 1;
                lockerOptions.PoolSize = 128;
                lockerOptions.PoolInitialFill = 64;
            });

            Options.Client ??= ManifestParseOptions.CreateDefaultClient();
        }
    }
}
