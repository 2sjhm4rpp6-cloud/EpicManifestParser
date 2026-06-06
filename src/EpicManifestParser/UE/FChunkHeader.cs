using System.Runtime.CompilerServices;

namespace EpicManifestParser.UE;

internal readonly struct FChunkHeader
{
	internal const int32 DefaultDataSizeUncompressed = 1024 * 1024;
	public const uint32 Magic = 0xB1FE3AA2;

	/// <summary>
	/// The version of this header data.
	/// </summary>
	public readonly EChunkVersion Version;
	/// <summary>
	/// The size of this header.
	/// </summary>
	public readonly int32 HeaderSize;
	/*/// <summary>
	/// The GUID for this data.
	/// </summary>
	public FGuid Guid;*/
	/// <summary>
	/// The size of this data compressed.
	/// </summary>
	public readonly int32 DataSizeCompressed;
	/// <summary>
	/// The size of this data uncompressed.
	/// </summary>
	public readonly int32 DataSizeUncompressed;
	/// <summary>
	/// How the chunk data is stored.
	/// </summary>
	public readonly EChunkStorageFlags StoredAs;
	/*/// <summary>
	/// What type of hash we are using.
	/// </summary>
	public EChunkHashFlags HashType;
	/// <summary>
	/// The FRollingHash hashed value for this chunk data.
	/// </summary>
	public uint64 RollingHash;
	/// <summary>
	/// The FSHA hashed value for this chunk data.
	/// </summary>
	public FSHAHash SHAHash;*/

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static FChunkHeader Parse(ManifestData data)
	{
		var reader = new ManifestReader(data);
		return new FChunkHeader(ref reader);
	}

	internal FChunkHeader(ref ManifestReader reader)
	{
		var startPos = reader.Position;
		var archiveSizeLeft = reader.Length - startPos;
		var expectedSerializedBytes = ChunkHeaderVersionSizes[(int32)EChunkVersion.Original];

		if (archiveSizeLeft >= expectedSerializedBytes)
		{
			var magic = reader.Read<uint32>();
			if (magic != Magic)
				throw new FileLoadException($"invalid chunk magic: 0x{magic:X}");

			Version = reader.Read<EChunkVersion>();
			HeaderSize = reader.Read<int32>();
			DataSizeCompressed = reader.Read<int32>();

			//Guid = reader.Read<FGuid>();
			//RollingHash = reader.Read<uint64>();
			reader.Position += FGuid.Size + sizeof(uint64);

			StoredAs = reader.Read<EChunkStorageFlags>();
			DataSizeUncompressed = DefaultDataSizeUncompressed;

			if (Version >= EChunkVersion.StoresShaAndHashType)
			{
				expectedSerializedBytes = ChunkHeaderVersionSizes[(int32)EChunkVersion.StoresShaAndHashType];
				if (archiveSizeLeft >= expectedSerializedBytes)
				{
					//SHAHash = reader.Read<FSHAHash>();
					//HashType = reader.Read<EChunkHashFlags>();
					reader.Position += FSHAHash.Size + sizeof(EChunkHashFlags);
				}

				if (Version >= EChunkVersion.StoresDataSizeUncompressed)
				{
					expectedSerializedBytes = ChunkHeaderVersionSizes[(int32)EChunkVersion.StoresDataSizeUncompressed];
					if (archiveSizeLeft >= expectedSerializedBytes)
					{
						DataSizeUncompressed = reader.Read<int32>();
					}
				}
			}
		}

		var success = reader.Position - startPos == expectedSerializedBytes;
		reader.Position = startPos + HeaderSize;
	}

	private static ReadOnlySpan<uint32> ChunkHeaderVersionSizes =>
	[
		// Dummy for indexing.
		0,
		// Original is 41 bytes (32b Magic, 32b Version, 32b HeaderSize, 32b DataSizeCompressed, 4x32b GUID, 64b Hash, 8b StoredAs).
		41,
		// StoresShaAndHashType is 62 bytes (328b Original, 160b SHA1, 8b HashType).
		62,
		// StoresDataSizeUncompressed is 66 bytes (496b StoresShaAndHashType, 32b DataSizeUncompressed).
		66
	];
}
