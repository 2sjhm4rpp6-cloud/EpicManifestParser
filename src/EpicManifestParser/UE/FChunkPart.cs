using System.Runtime.InteropServices;

namespace EpicManifestParser.UE;

/// <summary>
/// UE FChunkPart struct
/// </summary>
public readonly struct FChunkPart
{
    /*
    /// <summary>
    /// The GUID of the chunk containing this part.
    /// </summary>
    public FGuid Guid { get; }
    */
    /// <summary>
    /// The chunk containing this part.
    /// </summary>
    public FChunkInfo Chunk { get; }
    /// <summary>
    /// The offset of the first byte into the chunk.
    /// </summary>
    public uint32 Offset { get; }
    /// <summary>
    /// The size of this part.
    /// </summary>
    public uint32 Size { get; }

    /// <summary>
    /// The offset of the first byte into the file.
    /// </summary>
    public int64 FileOffset { get; }

    internal FChunkPart(FGuid guid, uint32 offset, uint32 size, int64 fileOffset, Dictionary<FGuid, FChunkInfo> chunks)
    {
        ref FChunkInfo? lookupChunk = ref CollectionsMarshal.GetValueRefOrAddDefault(chunks, guid, out bool exists);
        if (!exists)
        {
            lookupChunk = new FChunkInfo
            {
                Guid = guid
            };
        }

        Chunk = lookupChunk!;
        Offset = offset;
        Size = size;
        FileOffset = fileOffset;
    }

    internal FChunkPart(ref ManifestReader reader, int64 fileOffset, IReadOnlyDictionary<FGuid, FChunkInfo> chunks)
    {
        int startPos = reader.Position;
        int32 dataSize = reader.Read<int32>();

        FGuid guid = reader.Read<FGuid>();
        Chunk = chunks[guid];
        Offset = reader.Read<uint32>();
        Size = reader.Read<uint32>();
        FileOffset = fileOffset;

        reader.Position = startPos + dataSize;
    }
}
